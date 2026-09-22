using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rift.Contracts;

namespace Rift.Backend;

public sealed class ProfileCollector(RiftStore store, IRiotSource riot, ILogger<ProfileCollector> logger)
{
    public async Task Tick(CancellationToken token)
    {
        var leader = await store.TryCollectorLock(token); if (leader is null) return;
        ProfileJob? job = null;
        try
        {
            job = await store.Claim(token); if (job is null) return;
            if (job.Kind == "recent" && !store.HistoryBackfillEnabled)
                job = job with { Cursor = 0, From = DateTimeOffset.UnixEpoch };
            if (job.Cursor >= 10000) throw new SourceFailure("history_limit_reached", TimeSpan.FromHours(24));
            if (job.Puuid is null || job.Kind == "recent" && job.Cursor == 0 &&
                ((await store.Player(job.Platform, job.Puuid, token))?.RanksUpdatedAt ?? DateTimeOffset.MinValue) < DateTimeOffset.UtcNow.AddMinutes(-5))
            {
                var identity = await riot.Identity(job.Platform, job.RiotId, token);
                if (job.Puuid is not null && job.Puuid != identity.Puuid) throw new SourceFailure("identity_changed", TimeSpan.FromHours(6));
                await store.SetIdentity(job, identity, token); job = job with { Puuid = identity.Puuid };
            }
            if (job.Kind == "history" && !store.HistoryBackfillEnabled)
            {
                await store.FinishIdentity(job, token); return;
            }
            var region = ProfileInput.Regions[job.Platform];
            var ids = await riot.MatchIds(job, token); int missing = 0;
            foreach (var id in ids.Distinct())
            {
                token.ThrowIfCancellationRequested();
                if (await store.HasMatch(region, id, token)) { await store.SetUnavailable(job.Id, id, false, token); continue; }
                var match = await riot.Match(region, id, token);
                if (match is null) { missing++; await store.SetUnavailable(job.Id, id, true, token); continue; }
                if (match.Id != id || !match.Participants.Any(p => p.Puuid == job.Puuid)) throw new JsonException();
                await store.Ingest(region, match, token);
                await store.SetUnavailable(job.Id, id, false, token);
            }
            await store.CompletePage(job, ids.Length, token);
            logger.LogInformation("Page processed: {Ids} match IDs, {Missing} unavailable details", ids.Length, missing);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (job is not null) { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await store.Fail(job, "interrupted", TimeSpan.FromSeconds(5), cleanup.Token); }
        }
        catch (Exception ex) when (ex is SourceFailure or HttpRequestException or JsonException or ArgumentException or OperationCanceledException)
        {
            var code = ex is SourceFailure source ? source.Code : "source_unavailable";
            if (job is not null) await store.Fail(job, code, ex is SourceFailure failure ? failure.RetryAfter : TimeSpan.FromMinutes(2), token);
            logger.LogWarning("Collection deferred: {Code}", code); // No exception/URL/key/player in the log.
        }
        finally { await store.ReleaseCollectorLock(leader); }
    }
}

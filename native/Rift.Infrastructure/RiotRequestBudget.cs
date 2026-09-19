namespace Rift.Infrastructure;

// Conservative rolling windows per routing host. Existing requests are counted across profile reloads.
public sealed class RiotRequestBudget
{
    private readonly object sync = new();
    private readonly Dictionary<string, List<DateTimeOffset>> requests = [];
    private DateTimeOffset cooldown;
    public void PauseUntil(DateTimeOffset until) { lock (sync) { if (until > cooldown) cooldown = until; } }
    public TimeSpan Reserve(string host, DateTimeOffset now)
    {
        lock (sync)
        {
            if (cooldown > now) return cooldown - now;
            if (!requests.TryGetValue(host, out var history)) requests[host] = history = [];
            history.RemoveAll(t => now - t >= TimeSpan.FromMinutes(2));
            var recent = history.Where(t => now - t < TimeSpan.FromSeconds(1)).ToArray();
            var next = now;
            if (recent.Length >= 18) next = recent[0].AddSeconds(1);
            if (history.Count >= 90 && history[0].AddMinutes(2) > next) next = history[0].AddMinutes(2);
            if (next > now) return next - now;
            history.Add(now); return TimeSpan.Zero;
        }
    }
}

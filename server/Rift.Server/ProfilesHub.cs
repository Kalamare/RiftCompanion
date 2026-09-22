using Microsoft.AspNetCore.SignalR;
using Rift.Backend;
using Rift.Contracts;

namespace Rift.Server;

public sealed class ProfilesHub(RiftStore store) : Hub
{
    // One visible profile per connection bounds groups and work per client.
    public async Task Watch(string platform, string puuid)
    {
        ProfileInput.Player(platform, puuid);
        if (Context.Items.TryGetValue("profile", out var previous) && previous is string old)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, old);
        var name = Group(platform, puuid); Context.Items["profile"] = name;
        await Groups.AddToGroupAsync(Context.ConnectionId, name);
        await store.Touch(platform, puuid, Context.ConnectionAborted);
    }
    public static string Group(string platform, string puuid) => platform + ":" + puuid;
}

public sealed class ProfileNotifications(RiftStore store, IHubContext<ProfilesHub> hub, ILogger<ProfileNotifications> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long cursor = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var changes = await store.Changes(cursor, stoppingToken);
                foreach (var change in changes.GroupBy(x => (x.Platform, x.Puuid)).Select(g => g.Last()))
                    await hub.Clients.Group(ProfilesHub.Group(change.Platform, change.Puuid)).SendAsync("ProfileChanged", change, stoppingToken);
                if (changes.Length > 0) cursor = changes[^1].Sequence;
                if (changes.Length == 100) continue;
            }
            catch (Npgsql.NpgsqlException) { logger.LogWarning("Profile notifications: storage unavailable; retry pending"); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}

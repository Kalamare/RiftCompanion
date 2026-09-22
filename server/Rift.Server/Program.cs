using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using Rift.Backend;
using Rift.Contracts;
using Rift.Server;

var builder = WebApplication.CreateBuilder(args);
// Request paths contain player identifiers; avoid default per-request logging.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
var options = BackendOptions.Load(builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(NpgsqlDataSource.Create(options.ConnectionString));
builder.Services.AddSingleton<RiftStore>();
builder.Services.AddSignalR(o => { o.MaximumReceiveMessageSize = 4096; o.MaximumParallelInvocationsPerClient = 1; });
builder.Services.AddHostedService<ProfileNotifications>();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    // A global cap is sufficient for this private, single-operator development API.
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetFixedWindowLimiter("private", _ =>
        new FixedWindowRateLimiterOptions { PermitLimit = 180, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
await app.Services.GetRequiredService<RiftStore>().Initialize(app.Lifetime.ApplicationStopping);
var expectedKey = SHA256.HashData(Encoding.UTF8.GetBytes(options.AccessKey));
app.Use(async (context, next) =>
{
    if (context.Request.Path != "/health")
    {
        var key = context.Request.Headers["X-Rift-Key"].ToString();
        if (key.Length > 256 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)), expectedKey))
        { context.Response.StatusCode = 401; return; }
    }
    try { await next(context); }
    catch (WorkLimit limit)
    {
        context.Response.StatusCode = 429;
        context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling((limit.RetryAt-DateTimeOffset.UtcNow).TotalSeconds)).ToString();
        await context.Response.WriteAsJsonAsync(new { error = "work_budget_exhausted", nextAllowedAt = limit.RetryAt });
    }
    catch (ArgumentException)
    { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "invalid_input" }); }
    catch (NpgsqlException)
    { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "storage_unavailable" }); }
});
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "up", data = options.FixtureMode ? "synthetic" : "riot" }));
app.MapPost("/v1/profiles/lookup", async (ProfileLookup input, RiftStore store, CancellationToken token) =>
{
    var id = await store.Lookup(input, options.HistoryFrom, token);
    return Results.Accepted("/v1/lookups/" + id, new LookupReceipt(id));
});
app.MapGet("/v1/lookups/{id:guid}", async (Guid id, RiftStore store, CancellationToken token) =>
    await store.LookupState(id, token) is { } status ? Results.Ok(status) : Results.NotFound());
app.MapGet("/v1/players/{platform}/{puuid}", async (string platform, string puuid, RiftStore store, CancellationToken token) =>
    await store.Player(platform, puuid, token) is { } player ? Results.Ok(player) : Results.NotFound());
app.MapGet("/v1/players/{platform}/{puuid}/refresh", async (string platform, string puuid, RiftStore store, CancellationToken token) =>
    await store.Refresh(platform, puuid, false, TimeSpan.FromSeconds(options.RefreshCooldownSeconds), token) is { } state ? Results.Ok(state) : Results.NotFound());
app.MapPost("/v1/players/{platform}/{puuid}/refresh", async (string platform, string puuid, RiftStore store, CancellationToken token) =>
    await store.Refresh(platform, puuid, true, TimeSpan.FromSeconds(options.RefreshCooldownSeconds), token) is { } state ? Results.Ok(state) : Results.NotFound());
app.MapGet("/v1/players/{platform}/{puuid}/stats", async (string platform, string puuid, DateTimeOffset from, DateTimeOffset until, int queue, RiftStore store, CancellationToken token) =>
    await store.Stats(platform, puuid, from, until, queue, token) is { } stats ? Results.Ok(stats) : Results.NotFound());
app.MapGet("/v1/players/{platform}/{puuid}/profile", async (string platform, string puuid, int start, int count, long end, RiftStore store, CancellationToken token) =>
    await store.Profile(platform, puuid, start, count, end, token) is { } profile ? Results.Ok(profile) : Results.NotFound());
app.MapGet("/v1/matches/{platform}/{id}", async (string platform, string id, RiftStore store, CancellationToken token) =>
    await store.Details(platform, id, token) is { } details ? Results.Ok(details) : Results.NotFound());
app.MapHub<ProfilesHub>("/v1/events");
await app.RunAsync();

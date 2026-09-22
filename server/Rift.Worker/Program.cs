using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Quartz;
using Rift.Backend;
using Rift.Worker;

var builder = Host.CreateApplicationBuilder(args);
var options = BackendOptions.Load(builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(NpgsqlDataSource.Create(options.ConnectionString));
builder.Services.AddSingleton<RiftStore>();
builder.Services.AddSingleton<IRiotSource>(sp => options.FixtureMode ? new FixtureRiotSource() : new RiotSource(options, sp.GetRequiredService<RiftStore>()));
builder.Services.AddSingleton<ProfileCollector>();
builder.Services.AddQuartz(q =>
{
    var key = new JobKey("collect-profiles");
    q.AddJob<CollectProfilesJob>(j => j.WithIdentity(key));
    q.AddTrigger(t => t.ForJob(key).WithIdentity("collect-profiles-tick").StartNow()
        .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromSeconds(5)).RepeatForever()));
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
using var host = builder.Build();
await host.Services.GetRequiredService<RiftStore>().Initialize(CancellationToken.None);
await host.RunAsync();

namespace Rift.Worker
{
    [DisallowConcurrentExecution]
    public sealed class CollectProfilesJob(ProfileCollector collector) : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) => await collector.Tick(cancellationToken);
    }
}

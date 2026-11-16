using System.Net.Http.Headers;
using gitorch.Configuration;
using gitorch.Endpoints;
using gitorch.Middleware;
using gitorch.Services;
using gitorch.Services.Abstractions;
using gitorch.Services.Docker;
using gitorch.Services.Postgres;
using Microsoft.Extensions.Options;
using Npgsql;
using RunnerOrchestrator.Components;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<OrchestratorConfig>()
    .BindConfiguration("Orchestrator")
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<OrchestratorConfig>, OrchestratorConfigValidator>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<OrchestratorConfig>>().Value);

builder.Services.Configure<HostOptions>(opts =>
{
    var timeoutSec = builder.Configuration.GetValue("Orchestrator:ShutdownTimeoutSeconds", 30);
    opts.ShutdownTimeout = TimeSpan.FromSeconds(timeoutSec + 5);
});

builder.Services.AddHttpClient("GitHub", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RunnerOrchestrator", "1.0"));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
});

var dbProvider = builder.Configuration.GetValue<string>("Orchestrator:DatabaseProvider") ?? "Sqlite";
if (dbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton(sp =>
    {
        var cfg = sp.GetRequiredService<OrchestratorConfig>();
        return NpgsqlDataSource.Create(cfg.PostgresConnectionString);
    });
    builder.Services.AddSingleton<ISessionRepository, PostgresSessionRepository>();
    builder.Services.AddSingleton<IJobQueue, PostgresJobQueue>();
}
else
{
    builder.Services.AddSingleton<ISessionRepository, SessionRepository>();
    builder.Services.AddSingleton<IJobQueue, InMemoryJobQueue>();
}

builder.Services.AddSingleton<OrchestratorMetrics>();
builder.Services.AddSingleton<OrchestratorState>();
builder.Services.AddSingleton<DashboardAuthService>();
builder.Services.AddSingleton<IGitHubService, GitHubService>();
builder.Services.AddSingleton<IProfileResolver, RunnerProfileResolver>();

builder.Services.AddSingleton<IDockerNodePool, RoundRobinDockerNodePool>();
builder.Services.AddSingleton<DockerNetworkService>();
builder.Services.AddSingleton<DinDSidecarManager>();
builder.Services.AddSingleton<IRunnerContainerCustomizer, HostSocketCustomizer>();
builder.Services.AddSingleton<IRunnerContainerCustomizer, GpuDeviceCustomizer>();
builder.Services.AddSingleton<IRunnerContainerCustomizer, RepoCacheCustomizer>();
builder.Services.AddSingleton<IRunnerImageService, DockerImageService>();
builder.Services.AddSingleton<DockerRunnerEngine>();
builder.Services.AddSingleton<IRunnerEngine>(sp => sp.GetRequiredService<DockerRunnerEngine>());
builder.Services.AddSingleton<IRunnerLogReader>(sp => sp.GetRequiredService<DockerRunnerEngine>());

builder.Services.AddSingleton<RunnerWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RunnerWorker>());
builder.Services.AddHostedService<RetentionCleanupService>();

var signalRBuilder = builder.Services.AddSignalR();
var redisConn = builder.Configuration.GetValue<string>("Orchestrator:RedisConnectionString");
if (!string.IsNullOrWhiteSpace(redisConn))
{
    signalRBuilder.AddStackExchangeRedis(redisConn, opts =>
    {
        opts.Configuration.ChannelPrefix = RedisChannel.Literal("gitorch:");
    });
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseMiddleware<DashboardAuthMiddleware>();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAuthEndpoints();
app.MapWebhookEndpoints();
app.MapMetricsEndpoints();

app.Run();

public partial class Program;

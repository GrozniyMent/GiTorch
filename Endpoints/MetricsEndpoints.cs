using gitorch.Configuration;
using gitorch.Services;

namespace gitorch.Endpoints;

public static class MetricsEndpoints
{
    public static WebApplication MapMetricsEndpoints(this WebApplication app)
    {
        app.MapGet("/metrics", (
            OrchestratorMetrics metrics,
            OrchestratorState state,
            RunnerWorker worker,
            OrchestratorConfig config) =>
        {
            var body = metrics.RenderPrometheus(state, worker, config);
            return Results.Text(body, "text/plain; version=0.0.4; charset=utf-8");
        });

        return app;
    }
}


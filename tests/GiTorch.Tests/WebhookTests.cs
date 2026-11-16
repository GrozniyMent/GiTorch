using System.Net;
using System.Security.Cryptography;
using System.Text;
using Docker.DotNet;
using gitorch.Endpoints;
using gitorch.Models;
using gitorch.Services;
using gitorch.Services.Abstractions;
using gitorch.Services.Docker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GiTorch.Tests;

public sealed class WebhookTests : IClassFixture<WebhookTests.TestAppFactory>
{
    private const string TestSecret = "super-secret-webhook-key";
    private readonly HttpClient _client;
    private readonly TestAppFactory _factory;

    public WebhookTests(TestAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void VerifySignature_ValidatesHmacCorrectly()
    {
        var payload = "{\"action\":\"queued\"}"u8.ToArray();
        var sig = ComputeSignature(payload, TestSecret);

        Assert.True(WebhookEndpoints.VerifySignature(payload, sig, TestSecret));
        Assert.False(WebhookEndpoints.VerifySignature(payload, "sha256=deadbeef", TestSecret));
        Assert.False(WebhookEndpoints.VerifySignature(payload, "invalid", TestSecret));
        Assert.False(WebhookEndpoints.VerifySignature(payload, null, TestSecret));
    }

    [Fact]
    public async Task HealthAndPing_ReturnExpectedResponses()
    {
        var health = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        using var pingReq = new HttpRequestMessage(HttpMethod.Post, "/webhook");
        pingReq.Headers.Add("X-GitHub-Event", "ping");
        var pingResp = await _client.SendAsync(pingReq);
        Assert.Equal(HttpStatusCode.OK, pingResp.StatusCode);
        Assert.Contains("pong", await pingResp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Webhook_RejectsInvalidSignature_AndDeduplicatesQueuedJobs()
    {
        var json = """
            {
              "action": "queued",
              "workflow_job": { "id": 777, "labels": ["self-hosted", "linux"] },
              "repository": { "name": "demo", "owner": { "login": "acme", "type": "Organization" } },
              "installation": { "id": 11 }
            }
            """;
        var bytes = Encoding.UTF8.GetBytes(json);

        // Invalid signature -> 401
        using var badReq = new HttpRequestMessage(HttpMethod.Post, "/webhook");
        badReq.Headers.Add("X-GitHub-Event", "workflow_job");
        badReq.Headers.Add("X-Hub-Signature-256", "sha256=0000");
        badReq.Content = new ByteArrayContent(bytes);
        var badResp = await _client.SendAsync(badReq);
        Assert.Equal(HttpStatusCode.Unauthorized, badResp.StatusCode);

        // Valid signature -> 202 Accepted
        var sig = ComputeSignature(bytes, TestSecret);
        using var goodReq = new HttpRequestMessage(HttpMethod.Post, "/webhook");
        goodReq.Headers.Add("X-GitHub-Event", "workflow_job");
        goodReq.Headers.Add("X-Hub-Signature-256", sig);
        goodReq.Headers.Add("X-GitHub-Delivery", "delivery-777");
        goodReq.Content = new ByteArrayContent(bytes);
        var goodResp = await _client.SendAsync(goodReq);
        Assert.Equal(HttpStatusCode.Accepted, goodResp.StatusCode);

        // Duplicate delivery -> 200 duplicate ignored
        using var dupReq = new HttpRequestMessage(HttpMethod.Post, "/webhook");
        dupReq.Headers.Add("X-GitHub-Event", "workflow_job");
        dupReq.Headers.Add("X-Hub-Signature-256", sig);
        dupReq.Headers.Add("X-GitHub-Delivery", "delivery-777");
        dupReq.Content = new ByteArrayContent(bytes);
        var dupResp = await _client.SendAsync(dupReq);
        Assert.Equal(HttpStatusCode.OK, dupResp.StatusCode);
        Assert.Contains("duplicate ignored", await dupResp.Content.ReadAsStringAsync());

        // Completed/cancelled webhook cancels the queued job
        var cancelJson = """
            {
              "action": "completed",
              "workflow_job": { "id": 777, "labels": ["self-hosted"], "conclusion": "cancelled" },
              "repository": { "name": "demo", "owner": { "login": "acme", "type": "Organization" } },
              "installation": { "id": 11 }
            }
            """;
        var cancelBytes = Encoding.UTF8.GetBytes(cancelJson);
        using var cancelReq = new HttpRequestMessage(HttpMethod.Post, "/webhook");
        cancelReq.Headers.Add("X-GitHub-Event", "workflow_job");
        cancelReq.Headers.Add("X-Hub-Signature-256", ComputeSignature(cancelBytes, TestSecret));
        cancelReq.Content = new ByteArrayContent(cancelBytes);
        var cancelResp = await _client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.Accepted, cancelResp.StatusCode);

        var state = _factory.Services.GetRequiredService<OrchestratorState>();
        Assert.True(state.TryGetSession(777, out var session));
        Assert.Equal(RunnerStatus.Cancelled, session!.Status);
    }

    [Fact]
    public async Task Webhook_RejectsOversizedAndMalformedPayloads_AndSetsSecurityHeaders()
    {
        var oversized = new byte[1024 * 1024 + 64];
        using var largeReq = new HttpRequestMessage(HttpMethod.Post, "/webhook");
        largeReq.Headers.Add("X-GitHub-Event", "workflow_job");
        largeReq.Content = new ByteArrayContent(oversized);
        var largeResp = await _client.SendAsync(largeReq);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, largeResp.StatusCode);
        Assert.Equal("nosniff", largeResp.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", largeResp.Headers.GetValues("X-Frame-Options").Single());

        var malformed = "{\"action\":\"queued\",\"workflow_job\":{\"id\":1,\"labels\":[]}}"u8.ToArray();
        using var malformedReq = new HttpRequestMessage(HttpMethod.Post, "/webhook");
        malformedReq.Headers.Add("X-GitHub-Event", "workflow_job");
        malformedReq.Headers.Add("X-Hub-Signature-256", ComputeSignature(malformed, TestSecret));
        malformedReq.Content = new ByteArrayContent(malformed);
        var malformedResp = await _client.SendAsync(malformedReq);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResp.StatusCode);
    }

    private static string ComputeSignature(byte[] payload, string secret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return $"sha256={Convert.ToHexStringLower(hash)}";
    }

    public sealed class TestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"gitorch-test-{Guid.NewGuid():N}.db");
        private readonly string _pemPath = Path.Combine(Path.GetTempPath(), $"gitorch-key-{Guid.NewGuid():N}.pem");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            using (var rsa = RSA.Create(2048))
                File.WriteAllText(_pemPath, rsa.ExportRSAPrivateKeyPem());

            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Orchestrator:AppId"] = "999",
                    ["Orchestrator:WebhookSecret"] = TestSecret,
                    ["Orchestrator:PrivateKeyPathOrPem"] = _pemPath,
                    ["Orchestrator:DockerSocket"] = "http://localhost:2375",
                    ["Orchestrator:DatabasePath"] = _dbPath
                });
            });

            builder.ConfigureServices(services =>
            {
                // Do not run background Docker worker in HTTP integration tests
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IDockerNodePool>();
                services.AddSingleton<IDockerNodePool>(_ =>
                    new RoundRobinDockerNodePool(new DockerClientConfiguration(new Uri("http://localhost:2375")).CreateClient()));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_pemPath)) File.Delete(_pemPath);
        }
    }
}


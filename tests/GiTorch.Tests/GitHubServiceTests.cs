using System.Net;
using System.Security.Cryptography;
using System.Text;
using gitorch.Configuration;
using gitorch.Models;
using gitorch.Services;
using Xunit;

namespace GiTorch.Tests;

public sealed class GitHubServiceTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.github.com/")
        };
    }

    private static string CreateTestPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }

    [Fact]
    public async Task GenerateJitConfigAsync_CachesInstallationTokenAndUsesRepoOrOrgEndpoint()
    {
        var tokenCalls = 0;
        var endpoints = new List<string>();

        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath.TrimStart('/');
            endpoints.Add(path);

            if (path.EndsWith("/access_tokens"))
            {
                tokenCalls++;
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        $$"""{"token":"inst-tok-{{tokenCalls}}","expires_at":"{{DateTimeOffset.UtcNow.AddHours(1):O}}"}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (path.EndsWith("/generate-jitconfig"))
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        """{"encoded_jit_config":"jit-abc"}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var config = new OrchestratorConfig
        {
            AppId = "12345",
            PrivateKeyPathOrPem = CreateTestPem()
        };

        var service = new GitHubService(new StubHttpClientFactory(handler), config, new OrchestratorMetrics());

        var repoJit = await service.GenerateJitConfigAsync(
            "r1",
            new JobRequest(1, ["self-hosted"], 42, "octocat", "hello", IsOrganization: false),
            CancellationToken.None);

        var orgJit = await service.GenerateJitConfigAsync(
            "r2",
            new JobRequest(2, ["self-hosted"], 42, "my-org", "svc", IsOrganization: true),
            CancellationToken.None);

        Assert.Equal("jit-abc", repoJit);
        Assert.Equal("jit-abc", orgJit);
        Assert.Equal(1, tokenCalls); // cached across both calls for installation 42
        Assert.Contains("repos/octocat/hello/actions/runners/generate-jitconfig", endpoints);
        Assert.Contains("orgs/my-org/actions/runners/generate-jitconfig", endpoints);
    }

    [Fact]
    public async Task GenerateJitConfigAsync_IncrementsMetricOnApiFailure()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom")
        });

        var config = new OrchestratorConfig
        {
            AppId = "12345",
            PrivateKeyPathOrPem = CreateTestPem()
        };

        var metrics = new OrchestratorMetrics();
        var service = new GitHubService(new StubHttpClientFactory(handler), config, metrics);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.GenerateJitConfigAsync(
                "r1",
                new JobRequest(1, ["self-hosted"], 42, "octocat", "hello", IsOrganization: false),
                CancellationToken.None));

        Assert.Equal(1, metrics.GitHubApiErrors);
    }
}

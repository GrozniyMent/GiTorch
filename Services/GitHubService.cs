using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using gitorch.Configuration;
using gitorch.Models;
using gitorch.Services.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace gitorch.Services;

public sealed class GitHubService(
    IHttpClientFactory httpFactory,
    OrchestratorConfig config,
    OrchestratorMetrics metrics) : IGitHubService, IDisposable
{
    private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<long, (string Token, DateTimeOffset ExpiresAt)> _tokenCache = new();
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private RSA? _cachedRsa;

    public async Task<string> GenerateJitConfigAsync(string name, JobRequest job, CancellationToken ct)
    {
        var token = await GetInstallationTokenAsync(job.InstallationId, ct);
        using var client = CreateAuthenticatedClient(token);

        var body = new
        {
            name,
            runner_group_id = config.RunnerGroupId,
            labels = job.Labels.Length > 0 ? job.Labels : ["self-hosted", "linux", "x64"],
            work_folder = "_work"
        };

        var endpoint = job.IsOrganization
            ? $"orgs/{job.Owner}/actions/runners/generate-jitconfig"
            : $"repos/{job.Owner}/{job.RepoName}/actions/runners/generate-jitconfig";

        var resp = await client.PostAsJsonAsync(endpoint, body, ct);
        if (!resp.IsSuccessStatusCode)
        {
            metrics.IncrementGitHubApiError();
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"GitHub JIT API ({resp.StatusCode}): {err}");
        }

        var result = await resp.Content.ReadFromJsonAsync<JitConfigResponse>(ct);
        return result!.EncodedJitConfig;
    }

    private async Task<string> GetInstallationTokenAsync(long installationId, CancellationToken ct)
    {
        if (TryGetCachedToken(installationId, out var cachedToken))
            return cachedToken;

        await _authLock.WaitAsync(ct);
        try
        {
            if (TryGetCachedToken(installationId, out cachedToken))
                return cachedToken;

            using var client = CreateAuthenticatedClient(CreateAppJwt());
            var resp = await client.PostAsync($"app/installations/{installationId}/access_tokens", null, ct);
            if (!resp.IsSuccessStatusCode)
            {
                metrics.IncrementGitHubApiError();
                resp.EnsureSuccessStatusCode();
            }

            var data = await resp.Content.ReadFromJsonAsync<InstallationTokenResponse>(ct);
            _tokenCache[installationId] = (data!.Token, data.ExpiresAt);
            return data.Token;
        }
        finally
        {
            _authLock.Release();
        }
    }

    private bool TryGetCachedToken(long installationId, out string token)
    {
        if (_tokenCache.TryGetValue(installationId, out var cached) &&
            DateTimeOffset.UtcNow < cached.ExpiresAt - TokenRefreshMargin)
        {
            token = cached.Token;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private HttpClient CreateAuthenticatedClient(string bearerToken)
    {
        var client = httpFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    private RSA GetOrCreateRsa()
    {
        if (_cachedRsa != null)
            return _cachedRsa;

        var rsa = RSA.Create();
        var pem = File.Exists(config.PrivateKeyPathOrPem)
            ? File.ReadAllText(config.PrivateKeyPathOrPem)
            : config.PrivateKeyPathOrPem.Replace("\\n", "\n");

        rsa.ImportFromPem(pem);
        _cachedRsa = rsa;
        return rsa;
    }

    private string CreateAppJwt()
    {
        var rsa = GetOrCreateRsa();
        var securityKey = new RsaSecurityKey(rsa);
        var creds = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };

        var now = DateTime.UtcNow;
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateJwtSecurityToken(new SecurityTokenDescriptor
        {
            Issuer = config.AppId,
            IssuedAt = now.AddSeconds(-60),
            Expires = now.AddMinutes(9),
            SigningCredentials = creds
        });

        return handler.WriteToken(token);
    }

    public void Dispose()
    {
        _cachedRsa?.Dispose();
        _authLock.Dispose();
    }
}


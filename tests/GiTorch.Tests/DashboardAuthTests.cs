using gitorch.Configuration;
using gitorch.Middleware;
using gitorch.Services;
using Xunit;

namespace GiTorch.Tests;

public class DashboardAuthTests
{
    private static DashboardAuthService CreateAuth(string password = "secret-password") =>
        new(new OrchestratorConfig
        {
            WebhookSecret = "webhook-secret",
            DashboardUsername = "admin",
            DashboardPassword = password
        });

    [Fact]
    public void ValidateCredentials_ChecksUsernameAndPassword()
    {
        var auth = CreateAuth();
        Assert.True(auth.ValidateCredentials("admin", "secret-password"));
        Assert.False(auth.ValidateCredentials("admin", "wrong"));
        Assert.False(auth.ValidateCredentials("other", "secret-password"));
        Assert.False(auth.ValidateCredentials("", "secret-password"));
    }

    [Fact]
    public void SessionToken_RoundTripAndTamperRejection()
    {
        var auth = CreateAuth();
        var token = auth.CreateSessionToken("admin");

        Assert.True(auth.ValidateSessionToken(token));
        Assert.False(auth.ValidateSessionToken(token + "x"));
        Assert.False(auth.ValidateSessionToken("invalid.token"));
        Assert.False(auth.ValidateSessionToken(null));
    }

    [Fact]
    public void SanitizeReturnUrl_RejectsExternalRedirects()
    {
        Assert.Equal("/?status=ACTIVE", DashboardAuthMiddleware.SanitizeReturnUrl("/?status=ACTIVE"));
        Assert.Equal("/", DashboardAuthMiddleware.SanitizeReturnUrl("https://evil.example"));
        Assert.Equal("/", DashboardAuthMiddleware.SanitizeReturnUrl("//evil.example"));
        Assert.Equal("/", DashboardAuthMiddleware.SanitizeReturnUrl("/\\evil.example"));
        Assert.Equal("/", DashboardAuthMiddleware.SanitizeReturnUrl(""));
    }
}


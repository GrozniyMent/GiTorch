using System.Security.Cryptography;
using System.Text;
using gitorch.Configuration;

namespace gitorch.Services;

public sealed class DashboardAuthService(OrchestratorConfig config)
{
    public const string SessionCookieName = "gitorch_session";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    public bool IsAuthEnabled => !string.IsNullOrEmpty(config.DashboardPassword);

    public bool ValidateCredentials(string? username, string? password)
    {
        if (!IsAuthEnabled || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return false;

        return FixedTimeEquals(username, config.DashboardUsername) &&
               FixedTimeEquals(password, config.DashboardPassword);
    }

    public bool ValidateBasicAuthHeader(string? authHeader)
    {
        const string prefix = "Basic ";
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var encoded = authHeader[prefix.Length..].Trim();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var sep = decoded.IndexOf(':');
            if (sep <= 0)
                return false;

            return ValidateCredentials(decoded[..sep], decoded[(sep + 1)..]);
        }
        catch
        {
            return false;
        }
    }

    public string CreateSessionToken(string username)
    {
        var expiresUnix = DateTimeOffset.UtcNow.Add(SessionLifetime).ToUnixTimeSeconds();
        var payload = $"{username}\n{expiresUnix}";
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        var sigB64 = Base64UrlEncode(ComputeHmac(payloadB64));
        return $"{payloadB64}.{sigB64}";
    }

    public bool ValidateSessionToken(string? token)
    {
        if (!IsAuthEnabled || string.IsNullOrWhiteSpace(token))
            return false;

        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1)
            return false;

        var payloadB64 = token[..dot];
        var sigB64 = token[(dot + 1)..];

        var expectedSigB64 = Base64UrlEncode(ComputeHmac(payloadB64));
        if (!FixedTimeEquals(sigB64, expectedSigB64))
            return false;

        try
        {
            var payload = Encoding.UTF8.GetString(Base64UrlDecode(payloadB64));
            var parts = payload.Split('\n');
            if (parts.Length != 2)
                return false;

            if (!FixedTimeEquals(parts[0], config.DashboardUsername))
                return false;

            if (!long.TryParse(parts[1], out var expiresUnix))
                return false;

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expiresUnix;
        }
        catch
        {
            return false;
        }
    }

    private byte[] ComputeHmac(string data)
    {
        var keyMaterial = $"{config.WebhookSecret}:{config.DashboardPassword}:gitorch-session-v1";
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
        return HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(data));
    }

    private static bool FixedTimeEquals(string actual, string expected)
    {
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded
        };
        return Convert.FromBase64String(padded);
    }
}


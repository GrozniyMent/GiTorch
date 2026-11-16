using gitorch.Services;

namespace gitorch.Middleware;

public sealed class DashboardAuthMiddleware(RequestDelegate next, DashboardAuthService auth)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";

        var path = context.Request.Path;

        if (!auth.IsAuthEnabled ||
            path.StartsWithSegments("/health") ||
            path.StartsWithSegments("/metrics") ||
            path.StartsWithSegments("/webhook") ||
            path.StartsWithSegments("/login") ||
            path.StartsWithSegments("/logout") ||
            IsPublicAppAsset(path.Value))
        {
            await next(context);
            return;
        }

        var hasValidCookie =
            context.Request.Cookies.TryGetValue(DashboardAuthService.SessionCookieName, out var sessionToken) &&
            auth.ValidateSessionToken(sessionToken);

        var hasValidBasic = auth.ValidateBasicAuthHeader(context.Request.Headers.Authorization.FirstOrDefault());

        if (hasValidCookie || hasValidBasic)
        {
            await next(context);
            return;
        }

        if (IsBrowserNavigation(context.Request))
        {
            var returnUrl = SanitizeReturnUrl($"{context.Request.Path}{context.Request.QueryString}");
            var loginUrl = returnUrl == "/"
                ? "/login"
                : $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}";
            context.Response.Redirect(loginUrl);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private static bool IsPublicAppAsset(string? path) =>
        path != null &&
        path.StartsWith("/app", StringComparison.OrdinalIgnoreCase) &&
        (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".js", StringComparison.OrdinalIgnoreCase));

    private static bool IsBrowserNavigation(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method))
            return false;

        var accept = request.Headers.Accept.ToString();
        return accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    public static string SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) ||
            !returnUrl.StartsWith('/') ||
            returnUrl.StartsWith("//", StringComparison.Ordinal) ||
            returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return "/";
        }

        return returnUrl;
    }
}

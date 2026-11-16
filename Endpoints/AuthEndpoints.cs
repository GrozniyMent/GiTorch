using System.Net;
using gitorch.Middleware;
using gitorch.Services;
using RunnerOrchestrator.Components.Support;

namespace gitorch.Endpoints;

public static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/login", HandleGetLogin);
        app.MapPost("/login", HandlePostLoginAsync);
        app.MapPost("/logout", HandlePostLogout);
        return app;
    }

    private static IResult HandleGetLogin(HttpContext ctx, DashboardAuthService auth, string? returnUrl, string? error)
    {
        var target = DashboardAuthMiddleware.SanitizeReturnUrl(returnUrl);
        if (!auth.IsAuthEnabled)
            return Results.Redirect(target);

        if (ctx.Request.Cookies.TryGetValue(DashboardAuthService.SessionCookieName, out var token) &&
            auth.ValidateSessionToken(token))
        {
            return Results.Redirect(target);
        }

        var hasError = string.Equals(error, "1", StringComparison.Ordinal);
        return Results.Content(RenderLoginHtml(target, hasError), "text/html; charset=utf-8");
    }

    private static async Task<IResult> HandlePostLoginAsync(HttpContext ctx, DashboardAuthService auth)
    {
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var username = form["username"].ToString();
        var password = form["password"].ToString();
        var returnUrl = DashboardAuthMiddleware.SanitizeReturnUrl(form["returnUrl"].ToString());

        if (!auth.ValidateCredentials(username, password))
        {
            var errUrl = returnUrl == "/"
                ? "/login?error=1"
                : $"/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl)}";
            return Results.Redirect(errUrl);
        }

        var token = auth.CreateSessionToken(username);
        ctx.Response.Cookies.Append(DashboardAuthService.SessionCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = ctx.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/"
        });

        return Results.Redirect(returnUrl);
    }

    private static IResult HandlePostLogout(HttpContext ctx)
    {
        ctx.Response.Cookies.Delete(DashboardAuthService.SessionCookieName, new CookieOptions { Path = "/" });
        return Results.Redirect("/login");
    }

    private static string RenderLoginHtml(string returnUrl, bool hasError)
    {
        var encodedReturnUrl = WebUtility.HtmlEncode(returnUrl);
        var errorBanner = hasError
            ? """
              <div role="alert" class="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 font-mono text-xs text-rose-700 dark:text-rose-300">
                  Invalid username or password.
              </div>
              """
            : "";

        return $$"""
            <!DOCTYPE html>
            <html lang="en" class="bg-zinc-50 dark:bg-[#0a0a0b]">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <meta name="theme-color" content="#fafafa" media="(prefers-color-scheme: light)" />
                <meta name="theme-color" content="#0a0a0b" media="(prefers-color-scheme: dark)" />
                <title>Sign in — GiTorch</title>
                <link rel="preconnect" href="https://fonts.googleapis.com" />
                <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
                <link href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@700&display=swap" rel="stylesheet" />
                <script>
                    (function () {
                        const mq = window.matchMedia('(prefers-color-scheme: dark)');
                        const pref = localStorage.getItem('gitorch-theme');
                        const isDark = pref === 'dark' || (pref !== 'light' && mq.matches);
                        document.documentElement.classList.toggle('dark', isDark);
                        document.documentElement.style.colorScheme = isDark ? 'dark' : 'light';
                    })();
                </script>
                <script src="https://cdn.tailwindcss.com"></script>
                <script>
                    tailwind.config = {
                        darkMode: 'class',
                        theme: {
                            extend: {
                                fontFamily: {
                                    sans: ['Geist', 'ui-sans-serif', 'system-ui', 'sans-serif'],
                                    mono: ['"Geist Mono"', 'ui-monospace', 'SFMono-Regular', 'monospace'],
                                    brand: ['"Plus Jakarta Sans"', 'ui-sans-serif', 'system-ui', 'sans-serif'],
                                },
                            },
                        },
                    };
                </script>
                <style>
                    :root { --logo-bg: #ffffff; --logo-fg: #000000; }
                    html.dark { --logo-bg: #000000; --logo-fg: #ffffff; }
                    .aura {
                        background:
                            radial-gradient(600px 180px at 12% -40px, rgba(249, 115, 22, .10), transparent 70%),
                            radial-gradient(500px 160px at 88% -60px, rgba(14, 165, 233, .08), transparent 70%);
                    }
                    html.dark .aura {
                        background:
                            radial-gradient(600px 180px at 12% -40px, rgba(251, 146, 60, .14), transparent 70%),
                            radial-gradient(500px 160px at 88% -60px, rgba(56, 189, 248, .08), transparent 70%);
                    }
                </style>
            </head>
            <body class="min-h-screen bg-zinc-50 text-zinc-900 dark:bg-[#0a0a0b] dark:text-zinc-200 font-sans antialiased flex items-center justify-center p-5">
                <div class="aura pointer-events-none fixed inset-x-0 top-0 h-48" aria-hidden="true"></div>
                <main class="relative w-full max-w-sm rounded-xl border border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-950/80 p-7 shadow-xl dark:shadow-none">
                    <div class="flex items-center gap-3 mb-6">
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" class="h-10 w-10 shrink-0" aria-hidden="true">
                            {{DashboardFormatting.LogoInnerSvg}}
                        </svg>
                        <div>
                            <h1 class="font-brand text-xl font-bold tracking-tight text-zinc-900 dark:text-zinc-100">
                                Gi<span class="text-orange-600 dark:text-orange-400">Torch</span>
                            </h1>
                            <p class="font-mono text-[11px] text-zinc-500">Runner Orchestrator</p>
                        </div>
                    </div>

                    <form method="post" action="/login" class="space-y-4">
                        <input type="hidden" name="returnUrl" value="{{encodedReturnUrl}}" />
                        {{errorBanner}}
                        <div>
                            <label for="username" class="block font-mono text-[11px] uppercase tracking-wider text-zinc-500 mb-1.5">Username</label>
                            <input id="username"
                                   name="username"
                                   type="text"
                                   required
                                   autofocus
                                   autocomplete="username"
                                   class="w-full rounded-md border border-zinc-200 dark:border-zinc-800 bg-zinc-50 dark:bg-zinc-900/60 px-3 py-2 font-mono text-sm text-zinc-900 dark:text-zinc-100 placeholder:text-zinc-400 dark:placeholder:text-zinc-600 focus:border-zinc-400 dark:focus:border-zinc-600 focus:outline-none focus:ring-2 focus:ring-orange-500/20" />
                        </div>
                        <div>
                            <label for="password" class="block font-mono text-[11px] uppercase tracking-wider text-zinc-500 mb-1.5">Password</label>
                            <input id="password"
                                   name="password"
                                   type="password"
                                   required
                                   autocomplete="current-password"
                                   class="w-full rounded-md border border-zinc-200 dark:border-zinc-800 bg-zinc-50 dark:bg-zinc-900/60 px-3 py-2 font-mono text-sm text-zinc-900 dark:text-zinc-100 placeholder:text-zinc-400 dark:placeholder:text-zinc-600 focus:border-zinc-400 dark:focus:border-zinc-600 focus:outline-none focus:ring-2 focus:ring-orange-500/20" />
                        </div>
                        <button type="submit"
                                class="w-full rounded-md border border-zinc-900 dark:border-zinc-700 bg-zinc-900 dark:bg-zinc-100 px-4 py-2.5 font-mono text-xs font-semibold uppercase tracking-wider text-white dark:text-zinc-900 transition hover:bg-zinc-800 dark:hover:bg-white">
                            Sign in
                        </button>
                    </form>
                </main>
            </body>
            </html>
            """;
    }
}


using System.Globalization;
using gitorch.Models;

namespace RunnerOrchestrator.Components.Support;

public static class DashboardFormatting
{
    public static string ShortId(string? id, int length) =>
        string.IsNullOrEmpty(id) ? "—" : id.Length <= length ? id : id[..length];

    public static string FormatNodeEndpoint(string? endpoint) =>
        string.IsNullOrEmpty(endpoint) ? "local" : endpoint;

    public static string FormatSeconds(double seconds) =>
        $"{seconds.ToString("F1", CultureInfo.InvariantCulture)}s";

    public static double GetElapsedSeconds(RunnerSession s)
    {
        var start = s.StartedAt ?? s.QueuedAt;
        var end = s.FinishedAt ?? DateTimeOffset.UtcNow;
        var diff = (end - start).TotalSeconds;
        return diff < 0 ? 0 : diff;
    }

    public static string FormatDuration(RunnerSession s)
    {
        var sec = GetElapsedSeconds(s);
        if (sec < 60)
            return $"{sec.ToString("F1", CultureInfo.InvariantCulture)}s";

        var total = (int)sec;
        var h = total / 3600;
        var m = (total % 3600) / 60;
        var rem = total % 60;
        return h > 0 ? $"{h:D2}h {m:D2}m" : $"{m:D2}m {rem:D2}s";
    }

    public static double GetDurationPercent(RunnerSession s, double averageJobDurationSeconds)
    {
        var baseline = Math.Max(30.0, averageJobDurationSeconds * 3.0);
        var pct = (GetElapsedSeconds(s) / baseline) * 100.0;
        return Math.Clamp(pct, 3.0, 100.0);
    }

    public static bool IsActiveStatus(string status) =>
        status is RunnerStatus.Queued or RunnerStatus.Provisioning or RunnerStatus.Running;

    public static string GetImageBadgeClass(string imageStatus) => imageStatus switch
    {
        "READY" => "border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300",
        var s when s.Contains("FAILED", StringComparison.OrdinalIgnoreCase) => "border-rose-500/30 bg-rose-500/10 text-rose-700 dark:text-rose-300",
        var s when s.Contains("PULL", StringComparison.OrdinalIgnoreCase) => "border-sky-500/30 bg-sky-500/10 text-sky-700 dark:text-sky-300",
        _ => "border-zinc-300 dark:border-zinc-700 bg-zinc-100 dark:bg-zinc-900 text-zinc-600 dark:text-zinc-400"
    };

    public static string GetStatusPillClass(string status) => status switch
    {
        RunnerStatus.Queued => "border-sky-500/30 bg-sky-500/10 text-sky-700 dark:text-sky-300",
        RunnerStatus.Provisioning => "border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-300",
        RunnerStatus.Running => "border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300",
        RunnerStatus.Completed => "border-zinc-300 dark:border-zinc-700 bg-zinc-100 dark:bg-zinc-900 text-zinc-700 dark:text-zinc-300",
        RunnerStatus.Failed => "border-rose-500/30 bg-rose-500/10 text-rose-700 dark:text-rose-300",
        _ => "border-zinc-200 dark:border-zinc-800 bg-transparent text-zinc-500"
    };

    public static string GetStatusDotClass(string status) => status switch
    {
        RunnerStatus.Queued => "bg-sky-500 dark:bg-sky-400",
        RunnerStatus.Completed => "bg-emerald-500",
        RunnerStatus.Failed => "bg-rose-500",
        _ => "bg-zinc-400 dark:bg-zinc-600"
    };

    public static string GetDurationBarClass(string status) => status switch
    {
        RunnerStatus.Running => "bg-emerald-500 dark:bg-emerald-400",
        RunnerStatus.Provisioning => "bg-amber-500 dark:bg-amber-400",
        RunnerStatus.Queued => "bg-sky-500 dark:bg-sky-400",
        RunnerStatus.Completed => "bg-zinc-400 dark:bg-zinc-500",
        RunnerStatus.Failed => "bg-rose-500",
        _ => "bg-zinc-300 dark:bg-zinc-700"
    };

    public static LogEntry ParseLogLine(string rawLine, bool isStderr)
    {
        string? ts = null;
        var msg = rawLine;
        var spaceIdx = rawLine.IndexOf(' ');
        if (spaceIdx > 10 &&
            DateTimeOffset.TryParse(rawLine[..spaceIdx], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedTs))
        {
            ts = parsedTs.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            msg = rawLine[(spaceIdx + 1)..];
        }

        return new LogEntry(isStderr, ts, msg);
    }

    public static string FormatLogLine(LogEntry entry, bool showTimestamps)
    {
        var prefix = entry.IsStderr ? "[ERR] " : "";
        return showTimestamps && entry.Timestamp != null
            ? $"[{entry.Timestamp}] {prefix}{entry.Message}"
            : $"{prefix}{entry.Message}";
    }

    public static string GetLogLineClass(LogEntry entry)
    {
        if (entry.IsStderr ||
            entry.Message.Contains("##[error]", StringComparison.OrdinalIgnoreCase) ||
            entry.Message.Contains("FATAL", StringComparison.Ordinal) ||
            entry.Message.Contains("OOMKilled", StringComparison.OrdinalIgnoreCase))
            return "text-rose-400";

        if (entry.Message.Contains("Succeeded", StringComparison.OrdinalIgnoreCase) ||
            entry.Message.Contains("passed", StringComparison.OrdinalIgnoreCase) ||
            entry.Message.StartsWith("√", StringComparison.Ordinal) ||
            entry.Message.EndsWith("√", StringComparison.Ordinal))
            return "text-emerald-400";

        if (entry.Message.Contains("##[group]", StringComparison.OrdinalIgnoreCase))
            return "text-sky-300";

        if (entry.Message.Contains("##[endgroup]", StringComparison.OrdinalIgnoreCase))
            return "text-zinc-600";

        if (entry.Message.Contains("[gitorch]", StringComparison.OrdinalIgnoreCase))
            return "text-orange-300";

        return "text-zinc-300";
    }

    public static string GetToastBorderClass(string kind) => kind switch
    {
        "success" => "border-emerald-500/30",
        "error" => "border-rose-500/40",
        "warn" => "border-amber-500/30",
        _ => "border-zinc-200 dark:border-zinc-700"
    };

    public const string LogoInnerSvg = """
        <rect width="100" height="100" rx="22" fill="var(--logo-bg)" />
        <path d="M14.682 72.779A40 16-28 0 1 85.318 35.22" fill="none" stroke="var(--logo-fg)" stroke-width="7" stroke-linecap="round" />
        <path d="M50 12c13 12 17 22 9 32-4 4-14 4-18 0-8-10-4-20 9-32Z" fill="var(--logo-fg)" stroke="var(--logo-bg)" stroke-width="4" stroke-linejoin="round" />
        <path d="M50 28c4 5 5 10 0 14-5-4-4-9 0-14" fill="var(--logo-bg)" />
        <rect x="36" y="47" width="28" height="7" rx="3.5" fill="var(--logo-fg)" />
        <path d="M41 57h18l-4 29c-.5 2.5-2.5 4-5 4s-4.5-1.5-5-4Z" fill="var(--logo-fg)" />
        <g transform="rotate(-28 50 54)">
            <path d="M90 54a40 16 0 0 1-80 0" fill="none" stroke="var(--logo-bg)" stroke-width="13" stroke-linecap="round" />
            <path d="M90 54a40 16 0 0 1-80 0" fill="none" stroke="var(--logo-fg)" stroke-width="7" stroke-linecap="round" />
            <circle cx="10" cy="54" r="7.5" fill="var(--logo-fg)" />
            <circle cx="90" cy="54" r="7.5" fill="var(--logo-fg)" />
        </g>
        """;

    public static string GetToastDotClass(string kind) => kind switch
    {
        "success" => "bg-emerald-500 dark:bg-emerald-400",
        "error" => "bg-rose-500",
        "warn" => "bg-amber-500 dark:bg-amber-400",
        _ => "bg-sky-500 dark:bg-sky-400"
    };
}


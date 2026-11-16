namespace RunnerOrchestrator.Components.Support;

public sealed record LogEntry(bool IsStderr, string? Timestamp, string Message);

public sealed record ToastMessage(Guid Id, string Message, string Kind);

public sealed record StatusTab(string Key, string Label, int Count);


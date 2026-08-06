namespace ExecutiveDashboard.Providers;

public sealed record WorkIqCliInvocation(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout);

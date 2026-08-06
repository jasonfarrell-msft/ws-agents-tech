namespace ExecutiveDashboard.Providers;

public enum WorkIqCliExecutionStatus
{
    Available = 0,
    MissingExecutable = 1,
    NonZeroExit = 2,
    TimedOut = 3,
    Canceled = 4,
    FailedToStart = 5
}

public sealed record WorkIqCliExecutionResult(
    WorkIqCliExecutionStatus Status,
    string? StandardOutput = null,
    string? StandardError = null,
    int? ExitCode = null,
    string? Message = null)
{
    public static WorkIqCliExecutionResult Available(string standardOutput) => new(WorkIqCliExecutionStatus.Available, StandardOutput: standardOutput);

    public static WorkIqCliExecutionResult MissingExecutable(string message) => new(WorkIqCliExecutionStatus.MissingExecutable, Message: message);

    public static WorkIqCliExecutionResult NonZeroExit(int exitCode, string? standardError = null, string? message = null) =>
        new(WorkIqCliExecutionStatus.NonZeroExit, StandardError: standardError, ExitCode: exitCode, Message: message);

    public static WorkIqCliExecutionResult TimedOut(string message) => new(WorkIqCliExecutionStatus.TimedOut, Message: message);

    public static WorkIqCliExecutionResult Canceled(string message) => new(WorkIqCliExecutionStatus.Canceled, Message: message);

    public static WorkIqCliExecutionResult FailedToStart(string message) => new(WorkIqCliExecutionStatus.FailedToStart, Message: message);
}

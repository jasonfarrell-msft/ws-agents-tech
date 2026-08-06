namespace ExecutiveDashboard.Providers;

public interface IWorkIqCliRunner
{
    Task<WorkIqCliExecutionResult> RunAsync(WorkIqCliInvocation invocation, CancellationToken cancellationToken = default);
}

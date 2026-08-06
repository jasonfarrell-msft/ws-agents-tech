namespace ExecutiveDashboard.Providers;

public sealed class WorkIqAuthenticationException : Exception
{
    public WorkIqAuthenticationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

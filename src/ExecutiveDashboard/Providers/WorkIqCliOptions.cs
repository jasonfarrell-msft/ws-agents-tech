namespace ExecutiveDashboard.Providers;

public sealed class WorkIqCliOptions
{
    public const string SectionName = "WorkIQ:Cli";
    public const int MinimumTimeoutSeconds = 1;
    public const int MaximumTimeoutSeconds = 300;

    public bool Enabled { get; set; }

    public string ExecutablePath { get; set; } = "workiq";

    public int TimeoutSeconds { get; set; } = 60;

    public string[] AdditionalArguments { get; set; } = [];

    public bool HasUsableConfiguration =>
        !string.IsNullOrWhiteSpace(ExecutablePath)
        && TimeoutSeconds is >= MinimumTimeoutSeconds and <= MaximumTimeoutSeconds;

    public string UnavailableMessage =>
        "Work IQ CLI mode requires WorkIQ:Cli:ExecutablePath and WorkIQ:Cli:TimeoutSeconds from 1 to 300. Use WorkIQ:Mode=Sample for sample data.";
}

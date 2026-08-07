using ExecutiveDashboard.Models;

namespace ExecutiveDashboard.Providers;

internal static class WorkIqCliCommandBuilder
{
    public static WorkIqCliInvocation Build(MeetingQuery query, WorkIqOptions workIqOptions, WorkIqCliOptions cliOptions)
        => Build(workIqOptions, cliOptions, WorkIqMeetingPromptBuilder.BuildCalendarPrompt(query));

    public static WorkIqCliInvocation BuildDiarization(MeetingQuery query, WorkIqOptions workIqOptions, WorkIqCliOptions cliOptions)
        => Build(workIqOptions, cliOptions, WorkIqMeetingPromptBuilder.BuildDiarizationPrompt(query));

    public static WorkIqCliInvocation BuildEmailVolume(MeetingQuery query, WorkIqOptions workIqOptions, WorkIqCliOptions cliOptions)
        => Build(workIqOptions, cliOptions, WorkIqMeetingPromptBuilder.BuildEmailVolumePrompt(query));

    public static WorkIqCliInvocation BuildEmailConversationAnalysis(MeetingQuery query, WorkIqOptions workIqOptions, WorkIqCliOptions cliOptions)
        => Build(workIqOptions, cliOptions, WorkIqMeetingPromptBuilder.BuildEmailConversationPrompt(query));

    public static WorkIqCliInvocation BuildDecisionAnalysis(MeetingQuery query, WorkIqOptions workIqOptions, WorkIqCliOptions cliOptions)
        => Build(workIqOptions, cliOptions, WorkIqMeetingPromptBuilder.BuildDecisionAnalysisPrompt(query));

    private static WorkIqCliInvocation Build(
        WorkIqOptions workIqOptions,
        WorkIqCliOptions cliOptions,
        string prompt)
    {
        var executablePath = string.IsNullOrWhiteSpace(cliOptions.ExecutablePath) ? "workiq" : cliOptions.ExecutablePath.Trim();
        var arguments = cliOptions.AdditionalArguments
            .Where(argument => !string.IsNullOrWhiteSpace(argument))
            .Select(argument => argument.Trim())
            .ToList();

        if (!string.IsNullOrWhiteSpace(workIqOptions.TenantId))
        {
            arguments.Add("--tenant-id");
            arguments.Add(workIqOptions.TenantId.Trim());
        }

        arguments.AddRange(new[]
        {
            "ask",
            "--json",
            "-q",
            prompt
        });

        return new WorkIqCliInvocation(
            executablePath,
            arguments,
            TimeSpan.FromSeconds(cliOptions.TimeoutSeconds is >= WorkIqCliOptions.MinimumTimeoutSeconds and <= WorkIqCliOptions.MaximumTimeoutSeconds
                ? cliOptions.TimeoutSeconds
                : 60));
    }
}

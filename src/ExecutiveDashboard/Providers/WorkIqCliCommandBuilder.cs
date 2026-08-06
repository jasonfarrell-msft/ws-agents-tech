using ExecutiveDashboard.Models;

namespace ExecutiveDashboard.Providers;

internal static class WorkIqCliCommandBuilder
{
    public static WorkIqCliInvocation Build(MeetingQuery query, WorkIqOptions workIqOptions, WorkIqCliOptions cliOptions)
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
            "-q",
            WorkIqMeetingPromptBuilder.BuildMeetingPrompt(query)
        });

        return new WorkIqCliInvocation(
            executablePath,
            arguments,
            TimeSpan.FromSeconds(cliOptions.TimeoutSeconds is >= WorkIqCliOptions.MinimumTimeoutSeconds and <= WorkIqCliOptions.MaximumTimeoutSeconds
                ? cliOptions.TimeoutSeconds
                : 60));
    }
}

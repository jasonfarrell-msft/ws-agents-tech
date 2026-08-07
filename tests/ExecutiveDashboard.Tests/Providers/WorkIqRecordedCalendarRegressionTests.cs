using System.Security.Cryptography;
using System.Text;
using ExecutiveDashboard.Models;
using ExecutiveDashboard.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExecutiveDashboard.Tests.Providers;

public sealed class WorkIqRecordedCalendarRegressionTests
{
    private const string RecordedCalendarPrompt =
        "Query the signed-in user's Microsoft 365 calendar for meetings the user attended from Monday July 27, 2026 through Sunday August 2, 2026 inclusive.\n"
        + "Exclude all-day events, out-of-office events, declined meetings, canceled meetings, non-blocking events, and personal appointments.\n\n"
        + "Return only strict JSON with exactly this shape and no markdown or explanation:\n"
        + "{\"meetings\":[{\"startsAtUtc\":\"2026-08-03T13:00:00Z\",\"endsAtUtc\":\"2026-08-03T13:30:00Z\",\"isRecurring\":true}],\"availability\":{\"meetings\":\"available\",\"recurrence\":\"available\"}}\n\n"
        + "Values illustrate JSON types only. Include one row per attended meeting with actual UTC start and end times plus recurrence status.\n"
        + "Only return the three meeting properties shown and the two availability properties shown.\n"
        + "Do not include IDs, titles, people, summaries, transcript content, or extra properties.";
    private const string RecordedFixtureSha256 = "CB5E56ADE494652279A7859623E1E0D0AA9BF1E4CC082624E7B747637D959448";
    private static readonly (DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, bool IsRecurring)[] ExpectedRecordedMeetings =
    [
        (new DateTimeOffset(2026, 7, 27, 13, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 27, 13, 45, 0, TimeSpan.Zero), true),
        (new DateTimeOffset(2026, 7, 27, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 27, 14, 30, 0, TimeSpan.Zero), false),
        (new DateTimeOffset(2026, 7, 27, 15, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 27, 15, 30, 0, TimeSpan.Zero), false),
        (new DateTimeOffset(2026, 7, 27, 15, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 27, 16, 0, 0, TimeSpan.Zero), false),
        (new DateTimeOffset(2026, 7, 27, 16, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 27, 17, 0, 0, TimeSpan.Zero), false),
        (new DateTimeOffset(2026, 7, 27, 17, 5, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 27, 17, 30, 0, TimeSpan.Zero), false),
        (new DateTimeOffset(2026, 7, 28, 13, 40, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 28, 15, 30, 0, TimeSpan.Zero), false),
        (new DateTimeOffset(2026, 7, 28, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 28, 14, 30, 0, TimeSpan.Zero), false),
        (new DateTimeOffset(2026, 7, 28, 17, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 28, 17, 30, 0, TimeSpan.Zero), false),
        (new DateTimeOffset(2026, 7, 29, 12, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 29, 13, 0, 0, TimeSpan.Zero), true),
        (new DateTimeOffset(2026, 7, 29, 15, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 29, 16, 0, 0, TimeSpan.Zero), false),
        (new DateTimeOffset(2026, 7, 29, 19, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 29, 20, 0, 0, TimeSpan.Zero), false),
        (new DateTimeOffset(2026, 7, 30, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 30, 14, 50, 0, TimeSpan.Zero), false),
        (new DateTimeOffset(2026, 7, 30, 16, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 30, 17, 0, 0, TimeSpan.Zero), false),
        (new DateTimeOffset(2026, 7, 30, 19, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 30, 19, 30, 0, TimeSpan.Zero), true),
        (new DateTimeOffset(2026, 7, 31, 15, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 31, 16, 0, 0, TimeSpan.Zero), true)
    ];
    private static readonly MeetingQuery RecordedQuery = new(
        new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero),
        "sample-executive");

    [Fact]
    public async Task BuildCalendarPrompt_AndRecordedSanitizedCliResponse_RemainCompatible_ForJul27CompletedWeekRegression()
    {
        var prompt = WorkIqMeetingPromptBuilder.BuildCalendarPrompt(RecordedQuery);

        Assert.Equal(NormalizeMultiline(RecordedCalendarPrompt), NormalizeMultiline(prompt));

        // Sanitized recorded Work IQ CLI response regression artifact from the exact successful full-month prompt; not synthetic product sample data.
        var fixtureJson = await File.ReadAllTextAsync(GetFixturePath());
        var normalizedFixtureJson = NormalizeMultiline(fixtureJson);
        Assert.Equal(
            RecordedFixtureSha256,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedFixtureJson))));

        var parsed = WorkIqMeetingResponseParser.ParseStrictJson(normalizedFixtureJson);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, parsed.Status);
        Assert.NotNull(parsed.Response);
        Assert.Equal("available", parsed.Response!.Availability.Meetings);
        Assert.Equal("available", parsed.Response.Availability.Recurrence);
        Assert.Equal(ExpectedRecordedMeetings, parsed.Response.Meetings.Select(ToTuple).ToArray());

        var provider = new WorkIqMeetingDataProvider(
            new StubWorkIqMeetingDataClient(parsed),
            Options.Create(new WorkIqOptions { Mode = WorkIqProviderMode.Cli }),
            Options.Create(new WorkIqCliOptions { Enabled = true }),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-08-06T00:00:00Z")),
            NullLogger<WorkIqMeetingDataProvider>.Instance);

        var dataSet = await provider.GetMeetingsAsync(RecordedQuery);

        Assert.Equal(AvailabilityState.Available, dataSet.Availability);
        Assert.Equal(AvailabilityState.Available, dataSet.RecurrenceAvailability);
        Assert.Equal(ExpectedRecordedMeetings, dataSet.Meetings.Select(ToTuple).ToArray());
    }

    private static string GetFixturePath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "workiq-cli-calendar-jul-27-aug-2-2026.recorded.sanitized.json");

    private static string NormalizeMultiline(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static (DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, bool IsRecurring) ToTuple(WorkIqMeetingJson meeting) =>
        (
            meeting.StartsAtUtc,
            meeting.EndsAtUtc,
            meeting.IsRecurring ?? throw new Xunit.Sdk.XunitException("Expected recurrence projection for every recorded meeting row."));

    private static (DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, bool IsRecurring) ToTuple(Meeting meeting) =>
        (
            meeting.StartsAtUtc,
            meeting.EndsAtUtc,
            meeting.IsRecurring ?? throw new Xunit.Sdk.XunitException("Expected recurrence projection for every provider meeting row."));

    private sealed class StubWorkIqMeetingDataClient(WorkIqMeetingDataResult result) : IWorkIqMeetingDataClient
    {
        public Task<WorkIqMeetingDataResult> GetMeetingDataAsync(MeetingQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

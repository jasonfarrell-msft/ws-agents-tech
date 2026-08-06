using ExecutiveDashboard.Models;

namespace ExecutiveDashboard.Providers;

public sealed class UnavailableWorkIqMeetingDataProvider(TimeProvider timeProvider) : ILiveMeetingDataProvider
{
    private string _message =
        "Live mode is unavailable. Install and sign in to the Work IQ CLI, or configure AzureAd plus delegated Work IQ consent for this local app.";

    public UnavailableWorkIqMeetingDataProvider(TimeProvider timeProvider, string message)
        : this(timeProvider)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _message = message;
        }
    }

    public Task<MeetingDataSet> GetMeetingsAsync(MeetingQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            MeetingDataSet.Unavailable(
                query,
                "WorkIQ",
                timeProvider.GetUtcNow(),
                _message));
    }
}

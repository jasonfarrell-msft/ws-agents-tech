using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExecutiveDashboard.Models;
using Microsoft.Extensions.Options;

namespace ExecutiveDashboard.Providers;

public sealed class WorkIqChatClient(
    HttpClient httpClient,
    IWorkIqAccessTokenProvider tokenProvider,
    IOptions<WorkIqOptions> options) : IWorkIqChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WorkIqMeetingDataResult> GetMeetingDataAsync(MeetingQuery query, CancellationToken cancellationToken = default)
    {
        var configuration = options.Value;
        if (!configuration.HasUsableDirectConfiguration)
        {
            return WorkIqMeetingDataResult.Unavailable("Work IQ is not enabled or is missing endpoint, scope, or time zone configuration.");
        }

        string accessToken;
        try
        {
            accessToken = await tokenProvider.GetAccessTokenAsync(configuration.Scopes, cancellationToken);
        }
        catch (WorkIqAuthenticationException ex)
        {
            return WorkIqMeetingDataResult.AuthorizationFailed(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return WorkIqMeetingDataResult.AuthorizationFailed("Work IQ delegated token acquisition failed.");
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return WorkIqMeetingDataResult.AuthorizationFailed("Work IQ delegated token acquisition returned no access token.");
        }

        try
        {
            using var createConversationRequest = CreateJsonRequest(HttpMethod.Post, BuildUri(configuration.Endpoint, "conversations"), new { }, accessToken);
            using var createConversationResponse = await httpClient.SendAsync(createConversationRequest, cancellationToken);
            if (!createConversationResponse.IsSuccessStatusCode)
            {
                return ToFailure(createConversationResponse.StatusCode, "create a Work IQ conversation");
            }

            WorkIqConversationResponse? conversation;
            try
            {
                conversation = await createConversationResponse.Content.ReadFromJsonAsync<WorkIqConversationResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                return WorkIqMeetingDataResult.Malformed("Work IQ returned an invalid conversation response.");
            }

            if (string.IsNullOrWhiteSpace(conversation?.Id))
            {
                return WorkIqMeetingDataResult.Malformed("Work IQ did not return a conversation ID.");
            }

            var prompt = WorkIqMeetingPromptBuilder.BuildDirectMeetingPrompt(query);
            var chatBody = new WorkIqChatRequest(
                new WorkIqChatMessage(prompt),
                new WorkIqLocationHint(configuration.TimeZone),
                new WorkIqContextualResources(new WorkIqWebContext(false)));

            using var chatRequest = CreateJsonRequest(HttpMethod.Post, BuildUri(configuration.Endpoint, $"conversations/{Uri.EscapeDataString(conversation.Id)}/chat"), chatBody, accessToken);
            using var chatResponse = await httpClient.SendAsync(chatRequest, cancellationToken);
            if (!chatResponse.IsSuccessStatusCode)
            {
                return ToFailure(chatResponse.StatusCode, "ask Work IQ for meeting data");
            }

            WorkIqConversationResponse? copilotConversation;
            try
            {
                copilotConversation = await chatResponse.Content.ReadFromJsonAsync<WorkIqConversationResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                return WorkIqMeetingDataResult.Malformed("Work IQ returned an invalid chat response.");
            }

            var responseText = copilotConversation?.Messages?
                .LastOrDefault(message => !string.IsNullOrWhiteSpace(message.Text) && !string.Equals(message.Text.Trim(), prompt, StringComparison.Ordinal))?
                .Text;

            return WorkIqMeetingResponseParser.ParseStrictJson(responseText);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WorkIqMeetingDataResult.Unavailable("Work IQ request timed out before a valid response was received.");
        }
        catch (HttpRequestException)
        {
            return WorkIqMeetingDataResult.Unavailable("Work IQ request failed before a valid response was received.");
        }
    }

    private static HttpRequestMessage CreateJsonRequest<TBody>(HttpMethod method, Uri uri, TBody body, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static Uri BuildUri(string endpoint, string relativePath)
    {
        var baseUri = endpoint.EndsWith('/') ? new Uri(endpoint, UriKind.Absolute) : new Uri($"{endpoint}/", UriKind.Absolute);
        return new Uri(baseUri, relativePath);
    }

    private static WorkIqMeetingDataResult ToFailure(HttpStatusCode statusCode, string action) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? WorkIqMeetingDataResult.AuthorizationFailed($"Work IQ authorization failed while trying to {action}. Verify sign-in, delegated consent, and licensing.")
            : WorkIqMeetingDataResult.Unavailable($"Work IQ returned {(int)statusCode} while trying to {action}.");

    private sealed record WorkIqConversationResponse(string? Id, IReadOnlyList<WorkIqConversationMessage>? Messages);

    private sealed record WorkIqConversationMessage(string? Text);

    private sealed record WorkIqChatRequest(
        WorkIqChatMessage Message,
        WorkIqLocationHint LocationHint,
        WorkIqContextualResources ContextualResources);

    private sealed record WorkIqChatMessage(string Text);

    private sealed record WorkIqLocationHint(string TimeZone);

    private sealed record WorkIqContextualResources(WorkIqWebContext WebContext);

    private sealed record WorkIqWebContext(bool IsWebEnabled);
}

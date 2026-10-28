using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DomainService.Notification.Push
{
    /// <summary>What FCM said about one message.</summary>
    public enum FcmOutcome
    {
        Sent,

        /// <summary>The device token is no longer valid (404, UNREGISTERED, INVALID_ARGUMENT): delete the device.</summary>
        InvalidToken,

        /// <summary>429 or 5xx: try again later.</summary>
        Retry,

        /// <summary>401: the access token is not accepted.</summary>
        AuthFailed,

        Failed,
    }

    public sealed record FcmSendResult(FcmOutcome Outcome, int Status, TimeSpan? RetryAfter = null);

    /// <summary>One push to one device token. <paramref name="Data"/> values must be strings (FCM rule).</summary>
    public sealed record FcmMessage(string Token, string Title, string Body, IReadOnlyDictionary<string, string> Data);

    /// <summary>FCM HTTP v1 <c>messages:send</c>, one message per call.</summary>
    public interface IFcmClient
    {
        Task<FcmSendResult> SendAsync(string projectId, string accessToken, FcmMessage message, CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IFcmClient"/>
    public sealed class FcmClient : IFcmClient
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public FcmClient(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<FcmSendResult> SendAsync(string projectId, string accessToken, FcmMessage message, CancellationToken cancellationToken)
        {
            var url = $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(projectId)}/messages:send";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new
                {
                    message = new
                    {
                        token = message.Token,
                        notification = new { title = message.Title, body = message.Body },
                        data = message.Data,
                    },
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClientFactory.CreateClient(FcmAccessTokenProvider.HttpClientName).SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new FcmSendResult(FcmOutcome.Sent, (int)response.StatusCode);
            }

            var errorCode = await ErrorCodeAsync(response, cancellationToken);
            return Classify(response, errorCode);
        }

        internal static FcmSendResult Classify(HttpResponseMessage response, string? errorCode)
        {
            var status = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.NotFound || errorCode is "UNREGISTERED" or "INVALID_ARGUMENT")
                return new FcmSendResult(FcmOutcome.InvalidToken, status);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new FcmSendResult(FcmOutcome.AuthFailed, status);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500)
                return new FcmSendResult(FcmOutcome.Retry, status, RetryAfter(response));
            return new FcmSendResult(FcmOutcome.Failed, status);
        }

        private static TimeSpan? RetryAfter(HttpResponseMessage response)
        {
            var header = response.Headers.RetryAfter;
            if (header?.Delta is { } delta)
                return delta;
            if (header?.Date is { } date)
                return date - DateTimeOffset.UtcNow;
            return null;
        }

        /// <summary>
        /// The FCM error code: <c>details[].errorCode</c> (FcmError), else <c>error.status</c>. The body is never logged.
        /// </summary>
        private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                    return null;

                return DetailCode(error) ?? (error.TryGetProperty("status", out var status) ? status.GetString() : null);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? DetailCode(JsonElement error)
        {
            if (!error.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var detail in details.EnumerateArray())
            {
                if (detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("errorCode", out var code) && code.ValueKind == JsonValueKind.String)
                    return code.GetString();
            }

            return null;
        }
    }
}

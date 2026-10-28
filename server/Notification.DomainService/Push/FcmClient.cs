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

        /// <summary>
        /// The device token is no longer valid: 404, UNREGISTERED, or INVALID_ARGUMENT that names the registration token.
        /// Delete the device.
        /// </summary>
        InvalidToken,

        /// <summary>429 or 5xx: try again later.</summary>
        Retry,

        /// <summary>401: the access token is not accepted.</summary>
        AuthFailed,

        Failed,
    }

    /// <param name="ErrorCode">FCM's error code (for example INVALID_ARGUMENT), for the log. Never holds the token.</param>
    public sealed record FcmSendResult(FcmOutcome Outcome, int Status, TimeSpan? RetryAfter = null, string? ErrorCode = null);

    /// <summary>The parts of an FCM error body that decide what to do. <paramref name="NamesToken"/>: the error is about the registration token.</summary>
    public sealed record FcmError(string? Code, bool NamesToken)
    {
        public static readonly FcmError None = new(null, false);
    }

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
        private const string UnregisteredCode = "UNREGISTERED";
        private const string InvalidArgumentCode = "INVALID_ARGUMENT";
        private const string TokenField = "message.token";

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

            var error = await ReadErrorAsync(response, cancellationToken);
            return Classify(response, error);
        }

        /// <summary>
        /// Only a refused token deletes the device. INVALID_ARGUMENT also means a bad message from our side (payload too
        /// big, bad data key): then the device is kept, or one bad message would wipe every valid token.
        /// </summary>
        internal static FcmSendResult Classify(HttpResponseMessage response, FcmError error)
        {
            var status = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.NotFound || error.Code == UnregisteredCode
                || (error.Code == InvalidArgumentCode && error.NamesToken))
                return new FcmSendResult(FcmOutcome.InvalidToken, status, ErrorCode: error.Code);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new FcmSendResult(FcmOutcome.AuthFailed, status, ErrorCode: error.Code);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500)
                return new FcmSendResult(FcmOutcome.Retry, status, RetryAfter(response), error.Code);
            return new FcmSendResult(FcmOutcome.Failed, status, ErrorCode: error.Code);
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
        /// The FCM error code (<c>details[].errorCode</c> of FcmError, else <c>error.status</c>) and whether the error
        /// names the registration token (<c>google.rpc.BadRequest</c> field <c>message.token</c>, or the message says
        /// "registration token"). The body is never logged.
        /// </summary>
        private static async Task<FcmError> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                return ParseError(document.RootElement);
            }
            catch (JsonException)
            {
                return FcmError.None;
            }
        }

        private static FcmError ParseError(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                return FcmError.None;

            var details = error.TryGetProperty("details", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Where(d => d.ValueKind == JsonValueKind.Object).ToList()
                : [];
            var code = details.Select(d => StringOf(d, "errorCode")).FirstOrDefault(c => c is not null) ?? StringOf(error, "status");
            var namesToken = details.Any(NamesTokenField)
                || (StringOf(error, "message") ?? string.Empty).Contains("registration token", StringComparison.OrdinalIgnoreCase);
            return new FcmError(code, namesToken);
        }

        /// <summary>A <c>google.rpc.BadRequest</c> detail with a field violation on <c>message.token</c>.</summary>
        private static bool NamesTokenField(JsonElement detail) =>
            detail.TryGetProperty("fieldViolations", out var violations)
            && violations.ValueKind == JsonValueKind.Array
            && violations.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.Object && StringOf(v, "field") == TokenField);

        private static string? StringOf(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}

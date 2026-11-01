using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DomainService.Notification.Push;

namespace XUnitTest.Notification.Push
{
    /// <summary>Shared fakes for the push tests. Nothing here talks to Google.</summary>
    internal static class PushTestKit
    {
        public const string ProjectId = "demo-project";
        public const string ClientEmail = "push@demo-project.iam.gserviceaccount.com";

        private static readonly Lazy<string> PrivateKey = new(() =>
        {
            using var rsa = RSA.Create(2048);
            return rsa.ExportPkcs8PrivateKeyPem();
        });

        public static string ServiceAccountJson(string projectId = ProjectId, string type = "service_account", string? privateKey = null) =>
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["type"] = type,
                ["project_id"] = projectId,
                ["client_email"] = ClientEmail,
                ["private_key"] = privateKey ?? PrivateKey.Value,
                ["token_uri"] = "https://evil.example.com/token",
            });

        /// <summary>Push on with a key; Android and iOS on; Web on (with a web config) only when asked.</summary>
        public static PushSettingsRecord Settings(bool enabled = true, bool android = true, bool ios = true, bool web = false) => new()
        {
            ProjectId = ProjectId,
            Enabled = enabled,
            KeyUploadedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            KeyUploadedBy = "admin-1",
            Platforms = new PushPlatformSwitches { Android = android, Ios = ios, Web = web },
            WebConfigUploadedAt = web ? new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc) : null,
        };

        public static NotificationDevice Device(string userId, string token, string platform = "android") => new()
        {
            Id = NotificationDevice.IdOf(token),
            UserId = userId,
            Token = token,
            Platform = platform,
            Name = "Pixel 8",
            LastSeenAt = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc),
        };

        /// <summary>A session for the test key (the token provider is faked or real per test).</summary>
        public static FcmSession Session(string tenantId) =>
            new(tenantId, ServiceAccountKey.TryParse(ServiceAccountJson(), out _)!);

        public sealed class NoDelay : IPushRetryDelay
        {
            public List<TimeSpan> Waits { get; } = [];

            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                lock (Waits) Waits.Add(delay);
                return Task.CompletedTask;
            }
        }

        /// <summary>Answers the token URL with a fixed token and the FCM URL from a queue of answers (then 200).</summary>
        public sealed class FakeGoogle : HttpMessageHandler
        {
            private readonly Queue<Func<HttpResponseMessage>> _fcmAnswers = new();
            private readonly object _gate = new();

            public List<string> TokenRequests { get; } = [];
            public List<(string Url, string Authorization, string Body)> FcmRequests { get; } = [];
            public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;

            public FakeGoogle Then(HttpStatusCode status, string body = "{}", TimeSpan? retryAfter = null)
            {
                _fcmAnswers.Enqueue(() =>
                {
                    var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
                    if (retryAfter is { } wait)
                        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
                    return response;
                });
                return this;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                var url = request.RequestUri!.ToString();
                lock (_gate)
                {
                    if (url == "https://oauth2.googleapis.com/token")
                    {
                        TokenRequests.Add(body);
                        return new HttpResponseMessage(TokenStatus)
                        {
                            Content = new StringContent($"{{\"access_token\":\"access-{TokenRequests.Count}\",\"expires_in\":3600}}"),
                        };
                    }

                    FcmRequests.Add((url, request.Headers.Authorization?.ToString() ?? string.Empty, body));
                    return _fcmAnswers.Count > 0 ? _fcmAnswers.Dequeue()() : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                }
            }
        }

        public sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
        }
    }
}

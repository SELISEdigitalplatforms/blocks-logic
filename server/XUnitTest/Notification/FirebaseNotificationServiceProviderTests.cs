using System.Linq.Expressions;
using System.Net;
using DomainService.Entities;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using FirebaseConfiguration = DomainService.Configuration.FirebaseConfiguration;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Unit tests for <see cref="FirebaseNotificationServiceProvider"/>. The Firebase endpoint is
    /// replaced by a recording message handler, so the tests assert the outgoing requests and the
    /// failure handling without leaving the process.
    /// </summary>
    public class FirebaseNotificationServiceProviderTests : IDisposable
    {
        private const string FirebaseUri = "https://firebase.test/send";

        private readonly Mock<INotificationRepository> _repository = new();
        private readonly RecordingHandler _handler = new();
        private readonly HttpClient _httpClient;
        private readonly FirebaseNotificationServiceProvider _sut;

        public FirebaseNotificationServiceProviderTests()
        {
            _httpClient = new HttpClient(_handler);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["FirebaseUri"] = FirebaseUri })
                .Build();

            SetupFirebaseConfiguration(new FirebaseConfiguration { AuthorizationKey = "auth-key" });

            _sut = new FirebaseNotificationServiceProvider(
                Mock.Of<ILogger<FirebaseNotificationServiceProvider>>(),
                _repository.Object,
                configuration,
                _httpClient);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _handler.Dispose();
            GC.SuppressFinalize(this);
        }

        private void SetupFirebaseConfiguration(FirebaseConfiguration? configuration) =>
            _repository.Setup(r => r.GetItemAsync(
                    It.IsAny<Expression<Func<FirebaseConfiguration, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(configuration!);

        private static NotifyRequest Request(params string[] userIds) => new()
        {
            ConfigurationName = "cfg",
            UserIds = [.. userIds],
            DenormalizedPayload = "{\"title\":\"hello\"}",
            ContentAvailable = true,
        };

        private static NotificationConfiguration Configuration() => new()
        {
            Name = "cfg",
            ChannelToNotify = NotifierTypes.Firebase,
            NotificationType = NotificationReceiverTypes.UserSpecificReceiverType,
            NotifyMethod = "ReceiveNotification",
        };

        [Fact]
        public async Task Notify_PostsOneMessagePerRecipientToTheConfiguredEndpoint()
        {
            await _sut.Notify(Request("user-1", "user-2"), Configuration());

            _handler.Requests.Should().HaveCount(2);
            _handler.Requests.Should().OnlyContain(r => r.Uri == FirebaseUri);
            _handler.Requests.Should().OnlyContain(r => r.Authorization == "key=auth-key");
            _handler.Requests[0].Body.Should().Contain("/topics/user-1");
            _handler.Requests[1].Body.Should().Contain("/topics/user-2");
        }

        [Fact]
        public async Task Notify_CarriesTheDataNotificationAndContentFlagSections()
        {
            await _sut.Notify(Request("user-1"), Configuration());

            var body = _handler.Requests.Single().Body;
            body.Should().Contain("\"data\"");
            body.Should().Contain("\"notification\"");
            body.Should().Contain("\"content_available\":true");
        }

        [Fact]
        public async Task Notify_LosesTheValuesOfTheDenormalizedPayload()
        {
            // Documents current behaviour: the payload is parsed with Newtonsoft and then
            // serialised with System.Text.Json, which cannot read a JToken, so the values of
            // the payload never reach Firebase.
            await _sut.Notify(Request("user-1"), Configuration());

            _handler.Requests.Single().Body.Should().NotContain("hello");
        }

        [Fact]
        public async Task Notify_SendsNothingWhenFirebaseIsNotConfigured()
        {
            SetupFirebaseConfiguration(null);

            await _sut.Notify(Request("user-1"), Configuration());

            _handler.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Notify_SendsNothingWhenThereIsNoRecipient()
        {
            await _sut.Notify(Request(), Configuration());

            _handler.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Notify_StillSendsWhenTheDenormalizedPayloadIsMissing()
        {
            var request = Request("user-1");
            request.DenormalizedPayload = string.Empty;

            await _sut.Notify(request, Configuration());

            _handler.Requests.Should().ContainSingle("the missing payload is only warned about");
        }

        [Fact]
        public async Task Notify_StillSendsWhenTheAuthorizationKeyIsMissing()
        {
            SetupFirebaseConfiguration(new FirebaseConfiguration { AuthorizationKey = string.Empty });

            await _sut.Notify(Request("user-1"), Configuration());

            _handler.Requests.Should().ContainSingle();
            _handler.Requests.Single().Authorization.Should().Be("key=");
        }

        [Fact]
        public async Task Notify_ThrowsWithTheFirebaseResponseWhenTheCallIsRejected()
        {
            _handler.ResponseStatus = HttpStatusCode.BadRequest;
            _handler.ResponseBody = "InvalidRegistration";

            var act = () => _sut.Notify(Request("user-1", "user-2"), Configuration());

            (await act.Should().ThrowAsync<Exception>()).WithMessage("InvalidRegistration");
            _handler.Requests.Should().ContainSingle("the send stops at the first rejected recipient");
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public List<(string? Uri, string? Authorization, string Body)> Requests { get; } = [];

            public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;

            public string ResponseBody { get; set; } = "{}";

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                Requests.Add((
                    request.RequestUri?.ToString(),
                    request.Headers.TryGetValues("Authorization", out var values) ? string.Join(string.Empty, values) : null,
                    body));

                return new HttpResponseMessage(ResponseStatus) { Content = new StringContent(ResponseBody) };
            }
        }
    }
}

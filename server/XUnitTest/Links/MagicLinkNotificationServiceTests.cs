using System.Text.Json;
using Blocks.Genesis;
using DomainService.MagicLink.Service;
using DomainService.Shared.DTOs;
using DomainService.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Links
{
    /// <summary>
    /// Unit tests for <see cref="MagicLinkNotificationService"/>. Every method is fire-and-forget
    /// from the caller's point of view, so what matters is that a notification is only sent when
    /// there is a subscriber to send it to, that the payload carries the right shape, and that a
    /// transport failure never escapes into the operation that triggered it.
    /// </summary>
    public class MagicLinkNotificationServiceTests : IDisposable
    {
        private const string Filter = "subscription-1";

        private readonly Mock<ICryptoService> _crypto = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IHttpHelperServices> _http = new();
        private readonly List<(object Body, string Url, Dictionary<string, string> Headers)> _posts = [];

        public MagicLinkNotificationServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                "tenant-1", null, "user-9", true, null, null,
                DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, "", "tenant-1"));

            _crypto.Setup(c => c.Hash(It.IsAny<string>(), It.IsAny<string>())).Returns("hashed-secret");
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);

            _http.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                     It.IsAny<object>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                     It.IsAny<string>(), It.IsAny<string>()))
                 .Callback<object, string, Dictionary<string, string>, string, string>(
                     (body, url, headers, _, _) => _posts.Add((body, url, headers)))
                 .ReturnsAsync((new NotificationResponse { isSuccess = true }, string.Empty));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private MagicLinkNotificationService CreateService(
            Dictionary<string, string?>? settings = null) => new(
                new Mock<ILogger<MagicLinkNotificationService>>().Object,
                _crypto.Object,
                _tenants.Object,
                new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>
                {
                    ["RootTenantId"] = "root",
                    ["NotificationServiceUrl"] = "https://notify.example.com/send",
                    ["BlocksAppNotificationReceiver"] = "magic-link-receiver",
                }).Build(),
                _http.Object);

        /// <summary>Reads the anonymous request body back through JSON.</summary>
        private static JsonElement BodyOf((object Body, string Url, Dictionary<string, string> Headers) post) =>
            JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(post.Body));

        private JsonElement PayloadOf(int index = 0)
        {
            var denormalized = BodyOf(_posts[index]).GetProperty("DenormalizedPayload").GetString();
            return JsonSerializer.Deserialize<JsonElement>(denormalized!);
        }

        // ---- the subscriber guard ----

        [Fact]
        public async Task NotifyLinkCreatedEvent_SendsNothingWithoutASubscriber()
        {
            await CreateService().NotifyLinkCreatedEvent(true, "link-1", "https://s.io/a", null, "tenant-1");

            _posts.Should().BeEmpty();
        }

        [Fact]
        public async Task NotifyLinkCreatedEvent_SendsNothingForAnEmptySubscriber()
        {
            await CreateService().NotifyLinkCreatedEvent(true, "link-1", "https://s.io/a", "", "tenant-1");

            _posts.Should().BeEmpty();
        }

        [Fact]
        public async Task NotifyLinksCreatedEvent_SendsNothingWithoutASubscriber()
        {
            await CreateService().NotifyLinksCreatedEvent(true, 3, 0, null, "tenant-1");

            _posts.Should().BeEmpty();
        }

        [Fact]
        public async Task NotifyLinksRemovedEvent_SendsNothingWithoutASubscriber()
        {
            await CreateService().NotifyLinksRemovedEvent(true, 2, null, "tenant-1");

            _posts.Should().BeEmpty();
        }

        [Fact]
        public async Task NotifyActionExecutedEvent_SendsNothingWithoutASubscriber()
        {
            await CreateService().NotifyActionExecutedEvent(true, "link-1", 200, null, null, "tenant-1");

            _posts.Should().BeEmpty();
        }

        // ---- what each event carries ----

        [Fact]
        public async Task NotifyLinkCreatedEvent_CarriesTheLinkAndItsShortUri()
        {
            await CreateService().NotifyLinkCreatedEvent(true, "link-1", "https://s.io/a", Filter, "tenant-1");

            _posts.Should().ContainSingle();
            var data = PayloadOf().GetProperty("data");
            data.GetProperty("LinkId").GetString().Should().Be("link-1");
            data.GetProperty("ShortUri").GetString().Should().Be("https://s.io/a");
            PayloadOf().GetProperty("title").GetString().Should().Be("Magic Link Created");
        }

        [Fact]
        public async Task NotifyLinksCreatedEvent_CarriesBothCounts()
        {
            await CreateService().NotifyLinksCreatedEvent(true, 7, 2, Filter, "tenant-1");

            var data = PayloadOf().GetProperty("data");
            data.GetProperty("SuccessCount").GetInt32().Should().Be(7);
            data.GetProperty("FailureCount").GetInt32().Should().Be(2);
        }

        [Fact]
        public async Task NotifyLinksRemovedEvent_CarriesTheRemovedCount()
        {
            await CreateService().NotifyLinksRemovedEvent(true, 5, Filter, "tenant-1");

            PayloadOf().GetProperty("data").GetProperty("RemovedCount").GetInt32().Should().Be(5);
        }

        [Fact]
        public async Task NotifyActionExecutedEvent_CarriesTheStatusCodeAndError()
        {
            await CreateService().NotifyActionExecutedEvent(false, "link-1", 502, "upstream refused", Filter, "tenant-1");

            var data = PayloadOf().GetProperty("data");
            data.GetProperty("StatusCode").GetInt32().Should().Be(502);
            data.GetProperty("ErrorMessage").GetString().Should().Be("upstream refused");
        }

        // ---- the envelope ----

        [Fact]
        public async Task ASuccessfulEventDescribesItselfAsCompleted()
        {
            await CreateService().NotifyLinkCreatedEvent(true, "link-1", "https://s.io/a", Filter, "tenant-1");

            var payload = PayloadOf();
            payload.GetProperty("IsSuccess").GetBoolean().Should().BeTrue();
            payload.GetProperty("description").GetString().Should().Be("Magic Link Created completed successfully");
        }

        [Fact]
        public async Task AFailedEventDescribesItselfAsFailed()
        {
            await CreateService().NotifyLinkCreatedEvent(false, "link-1", "https://s.io/a", Filter, "tenant-1");

            var payload = PayloadOf();
            payload.GetProperty("IsSuccess").GetBoolean().Should().BeFalse();
            payload.GetProperty("description").GetString().Should().Be("Magic Link Created failed");
        }

        [Fact]
        public async Task TheNotificationIsAddressedToTheSubscriberAndTheCallingUser()
        {
            await CreateService().NotifyLinkCreatedEvent(true, "link-1", "https://s.io/a", Filter, "tenant-1");

            var body = BodyOf(_posts[0]);
            body.GetProperty("ConnectionId").GetString().Should().Be(Filter);
            body.GetProperty("ResponseKey").GetString().Should().Be(Filter);
            body.GetProperty("UserIds").EnumerateArray().Single().GetString().Should().Be("user-9");
        }

        [Fact]
        public async Task TheResponseValueMirrorsTheOutcome()
        {
            await CreateService().NotifyLinkCreatedEvent(false, "link-1", "https://s.io/a", Filter, "tenant-1");

            BodyOf(_posts[0]).GetProperty("ResponseValue").GetString().Should().Be("False");
        }

        [Fact]
        public async Task TheConfiguredReceiverIsUsed()
        {
            await CreateService().NotifyLinkCreatedEvent(true, "link-1", "https://s.io/a", Filter, "tenant-1");

            BodyOf(_posts[0]).GetProperty("ConfigurationName").GetString().Should().Be("magic-link-receiver");
        }

        [Fact]
        public async Task AMissingReceiverFallsBackToMagicLink()
        {
            var service = CreateService(new Dictionary<string, string?>
            {
                ["RootTenantId"] = "root",
                ["NotificationServiceUrl"] = "https://notify.example.com/send",
            });

            await service.NotifyLinkCreatedEvent(true, "link-1", "https://s.io/a", Filter, "tenant-1");

            BodyOf(_posts[0]).GetProperty("ConfigurationName").GetString().Should().Be("magic-link");
        }

        [Fact]
        public async Task TheRequestIsSentToTheConfiguredNotificationService()
        {
            await CreateService().NotifyLinkCreatedEvent(true, "link-1", "https://s.io/a", Filter, "tenant-1");

            _posts[0].Url.Should().Be("https://notify.example.com/send");
        }

        [Fact]
        public async Task TheRequestCarriesTheProjectKeyAndAHashedSecret()
        {
            _tenants.Setup(t => t.GetTenantByID("root")).Returns(new Tenant
            {
                TenantId = "root",
                Name = "Root",
                TenantSalt = "salt-1",
                DbConnectionString = "mongodb://localhost",
                JwtTokenParameters = new JwtTokenParameters
                {
                    IssueDate = DateTime.UtcNow,
                    PrivateCertificatePassword = "x",
                },
            });

            await CreateService().NotifyLinkCreatedEvent(true, "link-1", "https://s.io/a", Filter, "tenant-1");

            _posts[0].Headers["x-blocks-key"].Should().Be("root");
            _posts[0].Headers["Secret"].Should().Be("hashed-secret");
            // The secret is derived with the root tenant's own salt.
            _crypto.Verify(c => c.Hash("root", "salt-1"), Times.Once);
        }

        // ---- failures must not escape ----

        [Fact]
        public async Task ARejectedNotificationDoesNotThrow()
        {
            _http.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                     It.IsAny<object>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                     It.IsAny<string>(), It.IsAny<string>()))
                 .ReturnsAsync((new NotificationResponse { isSuccess = false, errors = "nope" }, string.Empty));

            var act = () => CreateService().NotifyLinkCreatedEvent(true, "link-1", "https://s.io/a", Filter, "tenant-1");

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task AMissingResponseDoesNotThrow()
        {
            _http.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                     It.IsAny<object>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                     It.IsAny<string>(), It.IsAny<string>()))
                 .ReturnsAsync(((NotificationResponse?)null, string.Empty));

            var act = () => CreateService().NotifyLinkCreatedEvent(true, "link-1", "https://s.io/a", Filter, "tenant-1");

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task ATransportFailureDoesNotEscapeIntoTheTriggeringOperation()
        {
            _http.Setup(h => h.MakeHttpPostRequest<NotificationResponse>(
                     It.IsAny<object>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                     It.IsAny<string>(), It.IsAny<string>()))
                 .ThrowsAsync(new HttpRequestException("notification service down"));

            var act = () => CreateService().NotifyLinkCreatedEvent(true, "link-1", "https://s.io/a", Filter, "tenant-1");

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task EveryEventReachesTheTransportWhenSubscribed()
        {
            var service = CreateService();

            await service.NotifyLinkCreatedEvent(true, "l", "u", Filter, "t");
            await service.NotifyLinksCreatedEvent(true, 1, 0, Filter, "t");
            await service.NotifyLinksRemovedEvent(true, 1, Filter, "t");
            await service.NotifyActionExecutedEvent(true, "l", 200, null, Filter, "t");

            _posts.Should().HaveCount(4);
        }
    }
}

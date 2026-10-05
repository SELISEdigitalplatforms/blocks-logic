using System.Linq.Expressions;
using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Phase 2 (#274): the provider queues one delivery command per device and returns; it never
    /// sends a push itself.
    /// </summary>
    public sealed class WebPushNotificationServiceProviderTests : IDisposable
    {
        private readonly Mock<INotificationRepository> _repo = new();
        private readonly Mock<IMessageClient> _bus = new();
        private readonly List<ConsumerMessage<WebPushDeliveryCommand>> _published = [];
        private readonly WebPushNotificationServiceProvider _sut;

        public WebPushNotificationServiceProviderTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                "tenant-1", null, "caller", true, null, null,
                DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, "", "tenant-1"));

            _bus.Setup(b => b.SendToConsumerAsync(It.IsAny<ConsumerMessage<WebPushDeliveryCommand>>()))
                .Callback<ConsumerMessage<WebPushDeliveryCommand>>(m => _published.Add(m))
                .Returns(Task.CompletedTask);

            _sut = new WebPushNotificationServiceProvider(
                Mock.Of<ILogger<WebPushNotificationServiceProvider>>(),
                _repo.Object,
                _bus.Object);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private void SetupSubs(params WebPushSubscription[] subs) =>
            _repo.Setup(r => r.GetItemsAsync(
                    It.IsAny<Expression<Func<WebPushSubscription, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((Expression<Func<WebPushSubscription, bool>> expr, string _) =>
                    subs.Where(expr.Compile()).ToList());

        private static NotifyRequest Request(params string[] userIds) => new()
        {
            ConfigurationName = "web-push-test",
            UserIds = [.. userIds],
            DenormalizedPayload = "{\"title\":\"Hi\"}",
        };

        private static NotificationConfiguration Configuration() => new()
        {
            Name = "web-push-test",
            ChannelToNotify = NotifierTypes.WebPush,
        };

        private static WebPushSubscription Sub(string userId, string endpoint) => new()
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Endpoint = endpoint,
            Keys = new WebPushSubscriptionKeys { P256dh = "p256-" + endpoint[^1], Auth = "auth-" + endpoint[^1] },
        };

        [Fact]
        public async Task Notify_PublishesOneCommandPerDevice_H1()
        {
            SetupSubs(
                Sub("U1", "https://push.example/a"),
                Sub("U1", "https://push.example/b"),
                Sub("U2", "https://push.example/c"));

            await _sut.Notify(Request("U1", "U2"), Configuration());

            _published.Should().HaveCount(3);
            _published.Should().OnlyContain(m => m.ConsumerName == WebPushConstants.DeliveryQueueName);
            _published.Select(m => m.Payload.SubscriptionEndpoint).Should().BeEquivalentTo(
                ["https://push.example/a", "https://push.example/b", "https://push.example/c"]);
        }

        [Fact]
        public async Task Notify_CommandCarriesEverythingTheWorkerNeeds_H1()
        {
            SetupSubs(Sub("U1", "https://push.example/a"));

            await _sut.Notify(Request("U1"), Configuration());

            var command = _published.Should().ContainSingle().Subject.Payload;
            command.UserId.Should().Be("U1");
            command.TenantId.Should().Be("tenant-1");
            command.KeysP256dh.Should().Be("p256-a");
            command.KeysAuth.Should().Be("auth-a");
            command.DenormalizedPayload.Should().Contain("Hi");
            command.Attempt.Should().Be(1);
        }

        [Fact]
        public async Task Notify_ZeroDevices_PublishesNothing_H5()
        {
            SetupSubs();

            await _sut.Notify(Request("U1"), Configuration());

            _published.Should().BeEmpty();
        }

        [Fact]
        public async Task Notify_NoUserIds_PublishesNothingAndSkipsTheRegistry_H5()
        {
            await _sut.Notify(Request(), Configuration());

            _published.Should().BeEmpty();
            _repo.Verify(r => r.GetItemsAsync(It.IsAny<Expression<Func<WebPushSubscription, bool>>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Notify_BlankUserIdsAreSkipped()
        {
            SetupSubs(Sub("U1", "https://push.example/a"));

            await _sut.Notify(Request("", " ", "U1"), Configuration());

            _published.Should().ContainSingle();
            _repo.Verify(r => r.GetItemsAsync(It.IsAny<Expression<Func<WebPushSubscription, bool>>>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Notify_NullUserIdsAndPayloadAreTolerated()
        {
            var request = new NotifyRequest { ConfigurationName = "web-push-test", UserIds = null!, DenormalizedPayload = null! };

            await _sut.Notify(request, Configuration());

            _published.Should().BeEmpty();
        }

        [Fact]
        public async Task Notify_SubscriptionWithoutKeys_PublishesEmptyKeys()
        {
            SetupSubs(new WebPushSubscription { Id = "1", UserId = "U1", Endpoint = "https://push.example/x", Keys = null! });

            await _sut.Notify(Request("U1"), Configuration());

            var command = _published.Should().ContainSingle().Subject.Payload;
            command.KeysP256dh.Should().BeEmpty();
            command.KeysAuth.Should().BeEmpty();
        }

        [Fact]
        public async Task Notify_WithoutTenantContext_PublishesEmptyTenant()
        {
            BlocksContext.SetContext(null);
            SetupSubs(Sub("U1", "https://push.example/a"));

            await _sut.Notify(Request("U1"), Configuration());

            _published.Should().ContainSingle().Which.Payload.TenantId.Should().BeEmpty();
        }

        [Fact]
        public async Task Notify_WhenTheBusIsUnavailable_ThrowsQueueUnavailable_C2()
        {
            SetupSubs(Sub("U1", "https://push.example/a"));
            _bus.Setup(b => b.SendToConsumerAsync(It.IsAny<ConsumerMessage<WebPushDeliveryCommand>>()))
                .ThrowsAsync(new InvalidOperationException("broker down"));

            var act = () => _sut.Notify(Request("U1"), Configuration());

            var thrown = await act.Should().ThrowAsync<WebPushQueueUnavailableException>();
            thrown.Which.Message.Should().Be(WebPushConstants.QueueErrorMessage);
            thrown.Which.InnerException.Should().BeOfType<InvalidOperationException>();
        }

        [Fact]
        public async Task Notify_NullRequest_Throws()
        {
            var act = () => _sut.Notify(null!, Configuration());

            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public void QueueUnavailableException_Constructors()
        {
            new WebPushQueueUnavailableException().Message.Should().Be(WebPushConstants.QueueErrorMessage);
            new WebPushQueueUnavailableException("m").Message.Should().Be("m");
        }
    }
}

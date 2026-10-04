using System.Linq.Expressions;
using DomainService.Entities;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WebPush;

namespace XUnitTest.Notification
{
    public class WebPushNotificationServiceProviderTests
    {
        private readonly Mock<INotificationRepository> _repo = new();
        private readonly Mock<IWebPushVapidKeyService> _vapid = new();
        private readonly Mock<IWebPushSender> _sender = new();
        private readonly WebPushNotificationServiceProvider _sut;
        private readonly List<(PushSubscription Sub, string Payload)> _sent = [];

        public WebPushNotificationServiceProviderTests()
        {
            _vapid.Setup(v => v.GetVapidDetailsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(("pub", "priv", "mailto:noreply@blocks.platform"));

            _sender.Setup(s => s.SendAsync(
                    It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()))
                .Returns<PushSubscription, string, VapidDetails, CancellationToken>((sub, payload, _, _) =>
                {
                    _sent.Add((sub, payload));
                    return Task.CompletedTask;
                });

            _sut = new WebPushNotificationServiceProvider(
                Mock.Of<ILogger<WebPushNotificationServiceProvider>>(),
                _repo.Object,
                _vapid.Object,
                _sender.Object);
        }

        private void SetupSubs(string userId, params WebPushSubscription[] subs) =>
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
            Keys = new WebPushSubscriptionKeys { P256dh = "p256", Auth = "auth" },
        };

        [Fact]
        public async Task Notify_SendsToEveryRegisteredDevice_H4()
        {
            SetupSubs("U1",
                Sub("U1", "https://push.example/a"),
                Sub("U1", "https://push.example/b"));

            await _sut.Notify(Request("U1"), Configuration());

            _sent.Should().HaveCount(2);
            _sent.Select(s => s.Sub.Endpoint).Should().BeEquivalentTo(
                ["https://push.example/a", "https://push.example/b"]);
            _sent.Should().OnlyContain(s => s.Payload.Contains("Hi"));
        }

        [Fact]
        public async Task Notify_ContinuesAfterOneDeviceFails_C3()
        {
            SetupSubs("U1",
                Sub("U1", "https://push.example/fail"),
                Sub("U1", "https://push.example/ok"));

            _sender.Setup(s => s.SendAsync(
                    It.Is<PushSubscription>(p => p.Endpoint.Contains("fail")),
                    It.IsAny<string>(), It.IsAny<VapidDetails>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("gone"));

            var act = async () => await _sut.Notify(Request("U1"), Configuration());

            await act.Should().NotThrowAsync();
            _sent.Should().ContainSingle(s => s.Sub.Endpoint.Contains("ok"));
        }

        [Fact]
        public async Task Notify_ZeroSubscriptionsIsSuccessNoOp_C4()
        {
            SetupSubs("U1");

            await _sut.Notify(Request("U1"), Configuration());

            _sent.Should().BeEmpty();
        }

        [Fact]
        public async Task Notify_NoUserIds_SendsNothing_C4()
        {
            await _sut.Notify(Request(), Configuration());
            _sent.Should().BeEmpty();
            _repo.Verify(r => r.GetItemsAsync(It.IsAny<Expression<Func<WebPushSubscription, bool>>>(), It.IsAny<string>()), Times.Never);
        }
    }
}

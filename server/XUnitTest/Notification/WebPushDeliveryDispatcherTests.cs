using DomainService.Notification;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Notification
{
    /// <summary>C4: a delivery waiting between attempts never blocks the next one.</summary>
    public class WebPushDeliveryDispatcherTests
    {
        private readonly Mock<IWebPushDeliveryService> _delivery = new();
        private readonly Mock<ILogger<WebPushDeliveryDispatcher>> _logger = new();

        private static WebPushDeliveryCommand Command(string user) => new()
        {
            SubscriptionEndpoint = "https://push.example/" + user,
            UserId = user,
            TenantId = "tenant-1",
        };

        [Fact]
        public async Task Dispatch_ReturnsWhileDeliveryIsStillWaiting_C4()
        {
            var slow = new TaskCompletionSource();
            var fastDone = new TaskCompletionSource();
            _delivery.Setup(d => d.DeliverAsync(It.Is<WebPushDeliveryCommand>(c => c.UserId == "slow"), It.IsAny<CancellationToken>()))
                     .Returns(slow.Task);
            _delivery.Setup(d => d.DeliverAsync(It.Is<WebPushDeliveryCommand>(c => c.UserId == "fast"), It.IsAny<CancellationToken>()))
                     .Returns(() => { fastDone.TrySetResult(); return Task.CompletedTask; });
            var sut = new WebPushDeliveryDispatcher(_delivery.Object, _logger.Object);

            sut.Dispatch(Command("slow"));
            sut.Dispatch(Command("fast"));

            await fastDone.Task.WaitAsync(TimeSpan.FromSeconds(5));
            sut.InFlightCount.Should().BeGreaterThanOrEqualTo(1, "the slow delivery is still waiting");

            slow.SetResult();
            await sut.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForAsync(() => sut.InFlightCount == 0);
            sut.InFlightCount.Should().Be(0);
        }

        [Fact]
        public async Task Dispatch_LogsAndContainsDeliveryExceptions()
        {
            var done = new TaskCompletionSource();
            _delivery.Setup(d => d.DeliverAsync(It.IsAny<WebPushDeliveryCommand>(), It.IsAny<CancellationToken>()))
                     .Returns(() => { done.TrySetResult(); return Task.FromException(new InvalidOperationException("boom")); });
            var sut = new WebPushDeliveryDispatcher(_delivery.Object, _logger.Object);

            sut.Dispatch(Command("u"));
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await sut.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));

            _logger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }

        [Fact]
        public void Dispatch_NullCommand_Throws()
        {
            var sut = new WebPushDeliveryDispatcher(_delivery.Object, _logger.Object);

            var act = () => sut.Dispatch(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public async Task DrainAsync_WithNothingInFlight_Completes()
        {
            var sut = new WebPushDeliveryDispatcher(_delivery.Object, _logger.Object);

            await sut.DrainAsync().WaitAsync(TimeSpan.FromSeconds(1));
            sut.InFlightCount.Should().Be(0);
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            for (var i = 0; i < 100 && !condition(); i++)
                await Task.Delay(10);
        }
    }
}

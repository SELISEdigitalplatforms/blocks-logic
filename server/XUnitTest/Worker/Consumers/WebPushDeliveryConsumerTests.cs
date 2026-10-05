using DomainService.Notification;
using FluentAssertions;
using Moq;
using Worker.Consumers.Notification;

namespace XUnitTest.Worker.Consumers
{
    public class WebPushDeliveryConsumerTests
    {
        [Fact]
        public async Task Consume_HandsTheCommandToTheDispatcherAndReturns()
        {
            var dispatcher = new Mock<IWebPushDeliveryDispatcher>();
            var sut = new WebPushDeliveryConsumer(dispatcher.Object);
            var command = new WebPushDeliveryCommand { UserId = "U1", TenantId = "t1", SubscriptionEndpoint = "https://push.example/a" };

            var task = sut.Consume(command);

            task.IsCompleted.Should().BeTrue();
            await task;
            dispatcher.Verify(d => d.Dispatch(command), Times.Once);
        }
    }
}

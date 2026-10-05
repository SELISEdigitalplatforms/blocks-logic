using DomainService.Notification;
using FluentAssertions;
using Workflow.DomainService.Utils;

namespace XUnitTest.Workflow
{
    public class LogicConstantsWebPushQueueTests
    {
        [Fact]
        public void RabbitMq_SubscribesToTheWebPushDeliveryQueue()
        {
            var config = LogicConstants.GetMessageConfiguration("amqp://guest:guest@localhost:5672");

            config.RabbitMqConfiguration!.ConsumerSubscriptions
                .Should().Contain(s => s.QueueName == WebPushConstants.DeliveryQueueName);
        }

        [Fact]
        public void AzureServiceBus_DeclaresTheWebPushDeliveryQueue()
        {
            var config = LogicConstants.GetMessageConfiguration("Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=x;SharedAccessKey=y");

            config.AzureServiceBusConfiguration!.Queues.Should().Contain(WebPushConstants.DeliveryQueueName);
        }

        [Fact]
        public void RetryPolicyIsFixedAt5sThen30sForThreeAttempts()
        {
            WebPushConstants.MaxAttempts.Should().Be(3);
            WebPushConstants.RetryDelays.Should().Equal(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
        }
    }
}

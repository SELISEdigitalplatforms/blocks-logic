using Blocks.Genesis;
using DomainService.Messaging;
using FluentAssertions;

namespace XUnitTest.Configurations
{
    /// <summary>
    /// Unit tests for MessageConfigurationHelper.
    ///
    /// The provider is inferred from the connection string rather than configured, so the
    /// scheme check is the only thing standing between a deployment and silently binding
    /// its consumers to the wrong broker. These tests pin that inference and the shape of
    /// each configuration it produces.
    /// </summary>
    public class MessageConfigurationHelperTests
    {
        private const string AmqpConnection = "amqp://guest:guest@localhost:5672";
        private const string AmqpsConnection = "amqps://guest:guest@rabbit.example.com:5671";
        private const string AzureConnection =
            "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=redacted";

        [Theory]
        [InlineData(AmqpConnection)]
        [InlineData(AmqpsConnection)]
        public void An_amqp_connection_string_selects_rabbitmq(string connectionString)
        {
            var configuration = MessageConfigurationHelper.GetMessageConfiguration(connectionString, "queue-a");

            configuration.RabbitMqConfiguration.Should().NotBeNull();
            configuration.AzureServiceBusConfiguration.Should().BeNull();
        }

        [Theory]
        [InlineData("AMQP://guest:guest@localhost:5672")]
        [InlineData("AmQpS://guest:guest@localhost:5671")]
        public void The_scheme_comparison_is_case_insensitive(string connectionString)
        {
            // The scheme is compared with OrdinalIgnoreCase, so a connection string that
            // arrives upper-cased from configuration must not fall through to Azure.
            var configuration = MessageConfigurationHelper.GetMessageConfiguration(connectionString, "queue-a");

            configuration.RabbitMqConfiguration.Should().NotBeNull();
        }

        [Theory]
        [InlineData(AzureConnection)]
        [InlineData("")]
        [InlineData("not-a-uri")]
        [InlineData("https://example.com")]
        public void Anything_that_is_not_amqp_falls_back_to_azure_service_bus(string connectionString)
        {
            // Azure is the default rather than an error, so a malformed or empty connection
            // string is indistinguishable from a real Azure one at this layer.
            var configuration = MessageConfigurationHelper.GetMessageConfiguration(connectionString, "queue-a");

            configuration.AzureServiceBusConfiguration.Should().NotBeNull();
            configuration.RabbitMqConfiguration.Should().BeNull();
        }

        [Fact]
        public void Rabbitmq_binds_one_subscription_per_queue_name_in_order()
        {
            var configuration = MessageConfigurationHelper.GetMessageConfiguration(
                AmqpConnection, "queue-a", "queue-b", "queue-c");

            configuration.RabbitMqConfiguration.ConsumerSubscriptions.Should().HaveCount(3);
        }

        [Fact]
        public void Azure_carries_every_queue_name_and_declares_no_topics()
        {
            var configuration = MessageConfigurationHelper.GetMessageConfiguration(
                AzureConnection, "queue-a", "queue-b");

            configuration.AzureServiceBusConfiguration.Queues.Should()
                .BeEquivalentTo(new[] { "queue-a", "queue-b" });
            configuration.AzureServiceBusConfiguration.Topics.Should().BeEmpty(
                "this helper only ever configures queues; topics are the caller's concern");
        }

        [Fact]
        public void Azure_pins_a_max_delivery_count_so_a_poison_message_stops_redelivering()
        {
            var configuration = MessageConfigurationHelper.GetMessageConfiguration(AzureConnection, "queue-a");

            configuration.AzureServiceBusConfiguration.QueueMaxDeliveryCount.Should().Be(10);
        }

        [Fact]
        public void No_queue_names_produces_an_empty_configuration_rather_than_throwing()
        {
            // params means a caller can omit the queues entirely. That is a misconfiguration
            // worth noticing, but it must not take the host down at startup.
            var rabbit = MessageConfigurationHelper.GetMessageConfiguration(AmqpConnection);
            var azure = MessageConfigurationHelper.GetMessageConfiguration(AzureConnection);

            rabbit.RabbitMqConfiguration.ConsumerSubscriptions.Should().BeEmpty();
            azure.AzureServiceBusConfiguration.Queues.Should().BeEmpty();
        }
    }
}

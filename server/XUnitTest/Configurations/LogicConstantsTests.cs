using DomainService.Workflow.Utils;
using FluentAssertions;

namespace XUnitTest.Configurations
{
    /// <summary>
    /// Unit tests for LogicConstants.
    ///
    /// The queue names are a wire contract: a publisher in a sibling repo writes to the
    /// literal string, so a rename here silently stops delivering rather than failing to
    /// build. blocks-localization shipped exactly that regression when a queue was renamed
    /// under a test that still asserted the old value, which is why these are pinned.
    /// </summary>
    public class LogicConstantsTests
    {
        private const string AmqpConnection = "amqp://<username>:<password>@localhost:5672";
        private const string AzureConnection =
            "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=redacted";

        [Fact]
        public void The_queue_names_are_the_ones_publishers_expect()
        {
            LogicConstants.NodeExecutionQueue.Should().Be("blocks_logic_workflow_node_execute_listener");
            LogicConstants.EmailTriggerQueue.Should().Be("blocks_logic_workflow_email_trigger_listener");
            LogicConstants.DataTriggerQueue.Should().Be("blocks_logic_workflow_data_trigger_listener");
            LogicConstants.LogicMailQueueName.Should().Be("blocks_email_listener");
            LogicConstants.MigrationCompletionTopic.Should().Be("blocks_migration_topic");
        }

        [Fact]
        public void The_auth_cookie_names_match_what_the_browser_is_sent()
        {
            LogicConstants.AccessTokenCookieName.Should().Be("access_token");
            LogicConstants.RefreshTokenCookieName.Should().Be("refresh_token");
        }

        [Theory]
        [InlineData(AmqpConnection)]
        [InlineData("amqps://<username>:<password>@rabbit.example.com:5671")]
        public void An_amqp_connection_string_selects_rabbitmq(string connectionString)
        {
            var configuration = LogicConstants.GetMessageConfiguration(connectionString);

            configuration.RabbitMqConfiguration.Should().NotBeNull();
            configuration.AzureServiceBusConfiguration.Should().BeNull();
        }

        [Theory]
        [InlineData(AzureConnection)]
        [InlineData("")]
        [InlineData("not-a-uri")]
        public void Anything_else_falls_back_to_azure_service_bus(string connectionString)
        {
            var configuration = LogicConstants.GetMessageConfiguration(connectionString);

            configuration.AzureServiceBusConfiguration.Should().NotBeNull();
            configuration.RabbitMqConfiguration.Should().BeNull();
        }

        [Fact]
        public void Azure_separates_the_migration_topic_from_the_four_queues()
        {
            var configuration = LogicConstants.GetMessageConfiguration(AzureConnection);

            configuration.AzureServiceBusConfiguration.Queues.Should().BeEquivalentTo(new[]
            {
                LogicConstants.NodeExecutionQueue,
                LogicConstants.EmailTriggerQueue,
                LogicConstants.DataTriggerQueue,
                LogicConstants.LogicMailQueueName,
            });
            configuration.AzureServiceBusConfiguration.Topics.Should()
                .BeEquivalentTo(new[] { LogicConstants.MigrationCompletionTopic });
        }

        [Fact]
        public void Rabbitmq_binds_the_migration_topic_as_a_fifth_queue()
        {
            // The two providers deliberately disagree: Azure models the migration
            // destination as a topic, RabbitMQ binds it as an ordinary queue. Pinned
            // because it looks like a copy-paste slip and is not one.
            var configuration = LogicConstants.GetMessageConfiguration(AmqpConnection);

            configuration.RabbitMqConfiguration.ConsumerSubscriptions.Should().HaveCount(5);
        }
    }
}

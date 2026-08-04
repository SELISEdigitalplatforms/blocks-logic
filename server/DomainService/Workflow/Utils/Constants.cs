using Blocks.Genesis;
using Mail.DomainService.Utilities;

namespace DomainService.Workflow.Utils
{
    public static class LogicConstants
    {
        public const string NodeExecutionQueue = "blocks_logic_workflow_node_execute_listener";
        public const string EmailTriggerQueue = "blocks_logic_workflow_email_trigger_listener";
        public const string DataTriggerQueue = "blocks_logic_workflow_data_trigger_listener";
        public const string LogicMailQueueName = "blocks_email_listener";
        public const string MigrationCompletionTopic = "blocks_migration_topic1";
        public const string AccessTokenCookieName = "access_token";
        public const string RefreshTokenCookieName = "refresh_token";

        private const string DefaultProvider = "azure";
        private const string RabbitMqProvider = "rabbitmq";

        public const string BlocsDomain = "seliseblocks.com";


        public static MessageConfiguration GetMessageConfiguration(string messageConnectionString)
        {
            var provider = GetProvider(messageConnectionString);

            return provider switch
            {
                RabbitMqProvider => CreateRabbitMqConfiguration(),
                _ => CreateAzureServiceBusConfiguration()
            };
        }

        private static string GetProvider(string messageConnectionString)
        {
            if (Uri.TryCreate(messageConnectionString, UriKind.Absolute, out var uri) &&
                (uri.Scheme.Equals("amqp", StringComparison.OrdinalIgnoreCase) ||
                 uri.Scheme.Equals("amqps", StringComparison.OrdinalIgnoreCase)))
            {
                return RabbitMqProvider;
            }
            return DefaultProvider;
        }

        private static MessageConfiguration CreateRabbitMqConfiguration()
        {
            return new MessageConfiguration
            {
                RabbitMqConfiguration = new RabbitMqConfiguration
                {
                    ConsumerSubscriptions = [ConsumerSubscription.BindToQueue(NodeExecutionQueue),
                                             ConsumerSubscription.BindToQueue(EmailTriggerQueue),
                                             ConsumerSubscription.BindToQueue(DataTriggerQueue),
                                             ConsumerSubscription.BindToQueue(LogicMailQueueName),
                                             ConsumerSubscription.BindToQueue(MigrationCompletionTopic)],

                }
            };
        }

        private static MessageConfiguration CreateAzureServiceBusConfiguration()
        {
            return new MessageConfiguration
            {
                AzureServiceBusConfiguration = new AzureServiceBusConfiguration
                {
                    Queues = [NodeExecutionQueue, EmailTriggerQueue, DataTriggerQueue, LogicMailQueueName],
                    Topics = [MigrationCompletionTopic]
                }
            };
        }
    }
}


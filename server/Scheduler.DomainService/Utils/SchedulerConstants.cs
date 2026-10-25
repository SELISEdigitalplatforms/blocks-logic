using Blocks.Genesis;

namespace Scheduler.DomainService.Utils
{
    public static class SchedulerConstants
    {
        public const string ScheduleJobRegistryQueueName = "blcoks_logic_scheduler_job_registry_listener";
        public const string WorkflowSchedulerTriggerQueue = "blocks_logic_workflow_scheduler_trigger_listener";

        /// <summary>An <c>amqp</c> or <c>amqps</c> URI is RabbitMQ; anything else is Azure Service Bus.</summary>
        public static bool IsRabbitMq(string messageConnectionString) =>
            Uri.TryCreate(messageConnectionString, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals("amqp", StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals("amqps", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Scheduler.Worker consumes the registry queue only. Genesis consumes every configured queue and drops
        /// messages it has no consumer for, so this host must not subscribe to anything the Worker handles.
        /// Registry events are applied one at a time, so two quick edits of a schedule land in order.
        /// </summary>
        public static MessageConfiguration GetSchedulerMessageConfiguration(string messageConnectionString)
        {
            if (IsRabbitMq(messageConnectionString))
            {
                return new MessageConfiguration
                {
                    RabbitMqConfiguration = new RabbitMqConfiguration
                    {
                        ConsumerSubscriptions = [ConsumerSubscription.BindToQueue(ScheduleJobRegistryQueueName)]
                    }
                };
            }

            return new MessageConfiguration
            {
                AzureServiceBusConfiguration = new AzureServiceBusConfiguration
                {
                    Queues = [ScheduleJobRegistryQueueName],
                    Topics = [],
                    MaxConcurrentCalls = 1
                }
            };
        }
    }
}

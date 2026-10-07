using Blocks.Genesis;
using Mail.DomainService.Utilities;
using Scheduler.DomainService.Utils;

namespace Workflow.DomainService.Utils
{
    public static class LogicConstants
    {
        public const string NodeExecutionQueue = "blocks_logic_workflow_node_execute_listener";

        /// <summary>
        /// Node messages are spread over this many queues (<c>{NodeExecutionQueue}_00</c>..<c>_15</c>), picked
        /// from the run id, so every step of one run goes to the same queue. Each queue is handled one message
        /// at a time (<see cref="NodeQueueLanes"/>) and the queues in parallel: a slow step blocks only its own
        /// queue, and a run stays sequential. Fixed on purpose; change the number here if more are needed.
        /// The old <see cref="NodeExecutionQueue"/> is kept and used too (user, 2026-10-07): it is one of the
        /// queues runs are spread over, so there are <see cref="AllNodeQueues"/> = 17 in all.
        /// </summary>
        public const int NodeQueueCount = 16;

        /// <summary>At most this many steps of one tenant work at the same time on a Worker (half of the queues).</summary>
        public const int TenantMaxActiveRuns = 8;

        public static readonly string[] NodeQueues =
            Enumerable.Range(0, NodeQueueCount).Select(i => $"{NodeExecutionQueue}_{i:00}").ToArray();

        /// <summary>The old queue first, then the 16 new ones: every queue a node message can be sent to.</summary>
        public static readonly string[] AllNodeQueues = [NodeExecutionQueue, .. NodeQueues];

        /// <summary>The queue of a run: the same for every step of it, on every service and machine.</summary>
        public static string NodeQueueFor(string executionId) => AllNodeQueues[NodeQueueIndex(executionId)];

        /// <summary>
        /// FNV-1a over the id. Not <c>string.GetHashCode</c>: that differs per process, and the Api and the
        /// Worker must pick the same queue for the same run.
        /// </summary>
        public static int NodeQueueIndex(string executionId)
        {
            var hash = 2166136261u;
            foreach (var c in executionId ?? string.Empty)
            {
                hash = unchecked((hash ^ c) * 16777619u);
            }
            return (int)(hash % (uint)AllNodeQueues.Length);
        }
        public const string EmailTriggerQueue = CommunicationConstants.EmailTriggerQueueName;
        public const string DataTriggerQueue = "blocks_logic_workflow_data_trigger_listener";
        public const string SchedulerTriggerQueue = SchedulerConstants.WorkflowSchedulerTriggerQueue;
        public const string WorkflowImportQueue = "blocks_logic_workflow_import_listener";
        public const string WorkflowImportNotificationConfigurationName = "workflow-import";
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
                    // The node queues are LAST: Genesis applies the prefetch once per subscription before
                    // consuming, so the last value counts for every consumer. They are taken in parallel by
                    // Genesis and made one-at-a-time per queue by NodeQueueLanes in the consumer.
                    ConsumerSubscriptions = [ConsumerSubscription.BindToQueue(EmailTriggerQueue),
                                             ConsumerSubscription.BindToQueue(DataTriggerQueue,10),
                                             ConsumerSubscription.BindToQueue(SchedulerTriggerQueue,10),
                                             ConsumerSubscription.BindToQueue(WorkflowImportQueue),
                                             ConsumerSubscription.BindToQueue(LogicMailQueueName),
                                             ConsumerSubscription.BindToQueue(CommunicationConstants.MailStatusQueueName),
                                             ConsumerSubscription.BindToQueue(MigrationCompletionTopic),
                                             ConsumerSubscription.BindToQueue(SchedulerConstants.ScheduleJobRegistryQueueName),
                                             .. AllNodeQueues
                                                 .Select(q => new ConsumerSubscription(q, string.Empty, 8, parallelProcessing: true))],

                }
            };
        }

        private static MessageConfiguration CreateAzureServiceBusConfiguration()
        {
            return new MessageConfiguration
            {
                AzureServiceBusConfiguration = new AzureServiceBusConfiguration
                {
                    Queues = [.. AllNodeQueues, EmailTriggerQueue, DataTriggerQueue, SchedulerTriggerQueue, WorkflowImportQueue, LogicMailQueueName, CommunicationConstants.MailStatusQueueName, SchedulerConstants.ScheduleJobRegistryQueueName],
                    Topics = [MigrationCompletionTopic],
                    QueuePrefetchCount = 40,
                    MaxConcurrentCalls = 40
                }
            };
        }
    }
}


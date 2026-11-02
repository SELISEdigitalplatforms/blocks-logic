using Blocks.Genesis;
using Blocks.Genesis.Notifications;
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
        /// <summary>Platform events Genesis forwards for the Blocks Event trigger (blocks-genesis-net#167).</summary>
        public const string BlocksEventTriggerQueue = BlocksEventTriggerConstants.QueueName;
        /// <summary>Node type of the Blocks Event trigger, on the client and in <c>PublishedMeta.TriggerNodes</c>.</summary>
        public const string BlocksEventNodeType = "blocksEvent";
        /// <summary>Node type of the Error Trigger, which starts a workflow when another workflow's production run fails.</summary>
        public const string ErrorTriggerNodeType = "errorTrigger";
        /// <summary>Failed production runs whose workflow has an alert set; the Worker starts the alert's error workflow.</summary>
        public const string ErrorTriggerQueue = "blocks_logic_workflow_error_trigger_listener";
        /// <summary>The <c>Category</c> of every trigger node.</summary>
        public const string TriggerCategory = "trigger";
        /// <summary>The execution context key that holds the trigger's items.</summary>
        public const string ExecutionInputKey = "Input";
        public const string SchedulerTriggerQueue = SchedulerConstants.WorkflowSchedulerTriggerQueue;
        public const string WorkflowImportQueue = "blocks_logic_workflow_import_listener";
        /// <summary>Fallback only, when WORKFLOW_NOTIFICATION_CONFIGURATION_NAME is not set; no environment creates it.</summary>
        public const string WorkflowImportNotificationConfigurationName = "workflow-import";
        public const string LogicMailQueueName = "blocks_email_listener";

        /// <summary>
        /// Function events (a function's <c>ctx.events.add('FunctionEvent', …)</c>). The Worker sends to it and consumes
        /// it: <c>FunctionEventConsumer</c> (the event trigger) starts the functions that subscribe to the event's name.
        /// </summary>
        public const string FunctionEventQueueName = "blocks_logic_function_event_listener";
        public const string MigrationCompletionTopic = "blocks_migration_topic1";
        public const string AccessTokenCookieName = "access_token";
        public const string RefreshTokenCookieName = "refresh_token";

        public const string BlocsDomain = "seliseblocks.com";


        /// <summary>Every endpoint, the schedule registry queue included: the Api and MailBoxSyncService.</summary>
        public static MessageConfiguration GetMessageConfiguration(string messageConnectionString) =>
            CreateConfiguration(messageConnectionString, includeRegistryQueue: true);

        /// <summary>
        /// The Worker: every endpoint except the schedule registry queue, which only Scheduler.Worker consumes
        /// (it hosts Hangfire). Same order and settings as <see cref="GetMessageConfiguration"/> otherwise.
        /// </summary>
        public static MessageConfiguration GetWorkerMessageConfiguration(string messageConnectionString) =>
            CreateConfiguration(messageConnectionString, includeRegistryQueue: false);

        private static MessageConfiguration CreateConfiguration(string messageConnectionString, bool includeRegistryQueue) =>
            SchedulerConstants.IsRabbitMq(messageConnectionString)
                ? CreateRabbitMqConfiguration(includeRegistryQueue)
                : CreateAzureServiceBusConfiguration(includeRegistryQueue);

        private static MessageConfiguration CreateRabbitMqConfiguration(bool includeRegistryQueue)
        {
            ConsumerSubscription[] registry = includeRegistryQueue
                ? [ConsumerSubscription.BindToQueue(SchedulerConstants.ScheduleJobRegistryQueueName)]
                : [];

            return new MessageConfiguration
            {
                RabbitMqConfiguration = new RabbitMqConfiguration
                {
                    // The node queues are LAST: Genesis applies the prefetch once per subscription before
                    // consuming, so the last value counts for every consumer. They are taken in parallel by
                    // Genesis and made one-at-a-time per queue by NodeQueueLanes in the consumer.
                    ConsumerSubscriptions = [ConsumerSubscription.BindToQueue(EmailTriggerQueue),
                                             ConsumerSubscription.BindToQueue(DataTriggerQueue,10),
                                             ConsumerSubscription.BindToQueue(BlocksEventTriggerQueue,10),
                                             ConsumerSubscription.BindToQueue(SchedulerTriggerQueue,10),
                                             ConsumerSubscription.BindToQueue(ErrorTriggerQueue,10),
                                             ConsumerSubscription.BindToQueue(WorkflowImportQueue),
                                             ConsumerSubscription.BindToQueue(LogicMailQueueName),
                                             // Consumed by the event trigger. It also makes sure the queue exists, which RabbitMQ needs
                                             // before a function can send to it (a message to a missing queue is dropped).
                                             ConsumerSubscription.BindToQueue(FunctionEventQueueName),
                                             ConsumerSubscription.BindToQueue(CommunicationConstants.MailStatusQueueName),
                                             ConsumerSubscription.BindToQueue(MigrationCompletionTopic),
                                             // Mobile push requests from every Blocks service (genesis). Only the Worker
                                             // registers a consumer; binding it in the other hosts declares the queue.
                                             ConsumerSubscription.BindToQueue(NotificationQueues.Push),
                                             .. registry,
                                             .. AllNodeQueues
                                                 .Select(q => new ConsumerSubscription(q, string.Empty, 8, parallelProcessing: true))],

                }
            };
        }

        private static MessageConfiguration CreateAzureServiceBusConfiguration(bool includeRegistryQueue)
        {
            string[] registry = includeRegistryQueue ? [SchedulerConstants.ScheduleJobRegistryQueueName] : [];

            return new MessageConfiguration
            {
                AzureServiceBusConfiguration = new AzureServiceBusConfiguration
                {
                    Queues = [.. AllNodeQueues, EmailTriggerQueue, DataTriggerQueue, BlocksEventTriggerQueue, SchedulerTriggerQueue, ErrorTriggerQueue, WorkflowImportQueue, LogicMailQueueName, FunctionEventQueueName, CommunicationConstants.MailStatusQueueName, NotificationQueues.Push, .. registry],
                    Topics = [MigrationCompletionTopic],
                    QueuePrefetchCount = 40,
                    MaxConcurrentCalls = 40
                }
            };
        }
    }
}


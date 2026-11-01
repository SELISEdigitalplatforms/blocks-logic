using Blocks.Genesis;
using FluentAssertions;
using Scheduler.DomainService.Utils;
using Workflow.DomainService.Utils;

namespace XUnitTest.Worker
{
    /// <summary>
    /// The Worker consumes everything the Api and MailBoxSyncService configure except the schedule registry
    /// queue, which Scheduler.Worker consumes. The order and every prefetch, parallel and concurrency setting
    /// stay the same, and a node queue stays the last RabbitMQ subscription: Genesis applies the last prefetch
    /// to every consumer on the channel.
    /// </summary>
    public class WorkerMessageConfigurationTests
    {
        private const string Rabbit = "amqp://guest:guest@localhost:5672";
        private const string Azure = "Endpoint=sb://blocks.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v";
        private const string Registry = SchedulerConstants.ScheduleJobRegistryQueueName;

        private static readonly (string Queue, ushort Prefetch, bool Parallel)[] FullRabbitMq =
        [
            ("blocks_workflow_email_trigger_listener", 5, false),
            ("blocks_logic_workflow_data_trigger_listener", 10, false),
            // Platform events Genesis forwards for the Blocks Event trigger (blocks-genesis-net#167); same prefetch as the data trigger.
            ("blocks_logic_workflow_blocks_event_trigger_listener", 10, false),
            ("blocks_logic_workflow_scheduler_trigger_listener", 10, false),
            ("blocks_logic_workflow_import_listener", 5, false),
            ("blocks_email_listener", 5, false),
            // Consumed by the event trigger (FunctionEventConsumer); also declares the queue, so a function event sent to it is not dropped.
            ("blocks_logic_function_event_listener", 5, false),
            ("blocks_mail_status_listener", 5, false),
            ("blocks_migration_topic1", 5, false),
            // Mobile push requests from every Blocks service (genesis); the Worker consumes them.
            ("blocks_notification_push_listener", 5, false),
            ("blcoks_logic_scheduler_job_registry_listener", 5, false),
            .. LogicConstants.AllNodeQueues.Select(q => (q, (ushort)8, true)),
        ];

        private static (string, ushort, bool)[] Shape(MessageConfiguration configuration) =>
            configuration.RabbitMqConfiguration!.ConsumerSubscriptions
                .Select(s => (s.QueueName, s.PrefetchCount, s.ParallelProcessing))
                .ToArray();

        [Fact]
        public void The_full_RabbitMq_configuration_is_unchanged()
        {
            Shape(LogicConstants.GetMessageConfiguration(Rabbit)).Should().Equal(FullRabbitMq);
            FullRabbitMq.Should().HaveCount(28);
        }

        [Fact]
        public void The_full_Azure_configuration_is_unchanged()
        {
            var azure = LogicConstants.GetMessageConfiguration(Azure).AzureServiceBusConfiguration!;

            azure.Queues.Should().Equal(
            [
                .. LogicConstants.AllNodeQueues,
                "blocks_workflow_email_trigger_listener",
                "blocks_logic_workflow_data_trigger_listener",
                "blocks_logic_workflow_blocks_event_trigger_listener",
                "blocks_logic_workflow_scheduler_trigger_listener",
                "blocks_logic_workflow_import_listener",
                "blocks_email_listener",
                "blocks_logic_function_event_listener",
                "blocks_mail_status_listener",
                "blocks_notification_push_listener",
                Registry,
            ]);
            azure.Queues.Should().HaveCount(27);
            azure.Topics.Should().Equal("blocks_migration_topic1");
            azure.QueuePrefetchCount.Should().Be(40);
            azure.MaxConcurrentCalls.Should().Be(40);
        }

        [Fact]
        public void The_Worker_RabbitMq_configuration_is_the_full_one_without_the_registry_queue()
        {
            var worker = Shape(LogicConstants.GetWorkerMessageConfiguration(Rabbit));

            worker.Should().Equal(FullRabbitMq.Where(s => s.Queue != Registry));
            worker.Should().HaveCount(27);
        }

        [Fact]
        public void The_Worker_Azure_configuration_is_the_full_one_without_the_registry_queue()
        {
            var full = LogicConstants.GetMessageConfiguration(Azure).AzureServiceBusConfiguration!;
            var worker = LogicConstants.GetWorkerMessageConfiguration(Azure).AzureServiceBusConfiguration!;

            worker.Queues.Should().Equal(full.Queues.Where(q => q != Registry));
            worker.Queues.Should().HaveCount(26);
            worker.Topics.Should().Equal(full.Topics);
            worker.QueuePrefetchCount.Should().Be(40);
            worker.MaxConcurrentCalls.Should().Be(40);
        }

        [Fact]
        public void A_node_queue_with_prefetch_8_stays_last_in_both_RabbitMq_configurations()
        {
            foreach (var configuration in new[] { LogicConstants.GetMessageConfiguration(Rabbit), LogicConstants.GetWorkerMessageConfiguration(Rabbit) })
            {
                var last = configuration.RabbitMqConfiguration!.ConsumerSubscriptions[^1];
                last.QueueName.Should().StartWith(LogicConstants.NodeExecutionQueue);
                last.PrefetchCount.Should().Be(8);
            }
        }

        [Fact]
        public void The_provider_check_is_the_scheduler_one()
        {
            LogicConstants.GetMessageConfiguration("amqps://broker").RabbitMqConfiguration.Should().NotBeNull();
            LogicConstants.GetWorkerMessageConfiguration("amqps://broker").RabbitMqConfiguration.Should().NotBeNull();
            LogicConstants.GetWorkerMessageConfiguration("garbage").AzureServiceBusConfiguration.Should().NotBeNull();
        }
    }
}

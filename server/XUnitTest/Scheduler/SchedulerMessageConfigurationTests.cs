using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.DomainService.Events;
using Scheduler.DomainService.Utils;

namespace XUnitTest.Scheduler
{
    /// <summary>
    /// Scheduler.Worker consumes the schedule registry queue and nothing else, one event at a time, and
    /// registers only the registry consumers: Genesis drops any message on a subscribed queue it has no
    /// consumer for, so the Worker's queues must stay out of this host.
    /// </summary>
    public class SchedulerMessageConfigurationTests
    {
        [Theory]
        [InlineData("amqp://guest:guest@localhost:5672")]
        [InlineData("AMQPS://broker.example.com")]
        public void RabbitMq_subscribes_to_the_registry_queue_only_one_at_a_time(string connection)
        {
            var configuration = SchedulerConstants.GetSchedulerMessageConfiguration(connection);

            configuration.AzureServiceBusConfiguration.Should().BeNull();
            var subscription = configuration.RabbitMqConfiguration!.ConsumerSubscriptions.Should().ContainSingle().Subject;
            subscription.QueueName.Should().Be("blcoks_logic_scheduler_job_registry_listener");
            subscription.PrefetchCount.Should().Be(5);
            subscription.ParallelProcessing.Should().BeFalse();
        }

        [Theory]
        [InlineData("Endpoint=sb://blocks.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v")]
        [InlineData("not a connection string")]
        [InlineData("")]
        public void Azure_listens_to_the_registry_queue_only_one_at_a_time(string connection)
        {
            var configuration = SchedulerConstants.GetSchedulerMessageConfiguration(connection);

            configuration.RabbitMqConfiguration.Should().BeNull();
            var azure = configuration.AzureServiceBusConfiguration!;
            azure.Queues.Should().Equal(SchedulerConstants.ScheduleJobRegistryQueueName);
            azure.Topics.Should().BeEmpty();
            azure.MaxConcurrentCalls.Should().Be(1);
        }

        [Theory]
        [InlineData("amqp://localhost", true)]
        [InlineData("amqps://broker.example.com:5671/vhost", true)]
        [InlineData("Endpoint=sb://blocks.servicebus.windows.net/", false)]
        [InlineData("http://localhost", false)]
        [InlineData("garbage", false)]
        [InlineData("", false)]
        public void IsRabbitMq_is_true_for_amqp_uris_only(string connection, bool expected)
        {
            SchedulerConstants.IsRabbitMq(connection).Should().Be(expected);
        }

        [Fact]
        public void The_scheduler_host_registers_only_the_registry_consumers_and_a_clock()
        {
            var services = new ServiceCollection();

            services.AddSchedulerServices();
            services.AddSchedulerWorkerServices();

            services.Where(d => d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(IConsumer<>))
                .Select(d => d.ServiceType.GetGenericArguments()[0])
                .Should().BeEquivalentTo([typeof(ScheduleJobUpsertedEvent), typeof(ScheduleJobDeletedEvent)]);
            services.Should().ContainSingle(d => d.ServiceType == typeof(TimeProvider));
        }

        [Fact]
        public void A_clock_registered_before_is_kept()
        {
            var services = new ServiceCollection();
            var clock = new FrozenClock();
            services.AddSingleton<TimeProvider>(clock);

            services.AddSchedulerWorkerServices();

            services.Should().ContainSingle(d => d.ServiceType == typeof(TimeProvider))
                .Which.ImplementationInstance.Should().BeSameAs(clock);
        }

        private sealed class FrozenClock : TimeProvider;
    }
}

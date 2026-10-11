using FluentAssertions;
using Workflow.DomainService.Utils;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Node messages go to 16 fixed queues picked from the run id; each queue is handled one step at a
    /// time and the queues in parallel; a tenant has at most 8 steps working at once on a Worker.
    /// </summary>
    public class NodeQueuesTests
    {
        [Fact]
        public void There_are_16_named_queues_and_the_old_one_is_kept()
        {
            LogicConstants.NodeQueues.Should().HaveCount(16);
            LogicConstants.NodeQueues[0].Should().Be("blocks_logic_workflow_node_execute_listener_00");
            LogicConstants.NodeQueues[15].Should().Be("blocks_logic_workflow_node_execute_listener_15");
            LogicConstants.TenantMaxActiveRuns.Should().Be(8);
            LogicConstants.AllNodeQueues.Should().HaveCount(17);
            LogicConstants.AllNodeQueues[0].Should().Be(LogicConstants.NodeExecutionQueue, "the old queue is kept and used");

            var asb = LogicConstants.GetMessageConfiguration("Endpoint=sb://x/").AzureServiceBusConfiguration!.Queues;
            asb.Should().Contain(LogicConstants.NodeExecutionQueue).And.Contain(LogicConstants.NodeQueues);

            var rabbit = LogicConstants.GetMessageConfiguration("amqp://localhost").RabbitMqConfiguration!.ConsumerSubscriptions;
            var node = rabbit.Where(s => s.QueueName.StartsWith(LogicConstants.NodeExecutionQueue, StringComparison.Ordinal)).ToList();
            node.Should().HaveCount(17).And.OnlyContain(s => s.ParallelProcessing);
            rabbit[^1].QueueName.Should().StartWith(LogicConstants.NodeExecutionQueue, "the last prefetch is the one Genesis applies");
        }

        [Fact]
        public void Every_step_of_a_run_goes_to_the_same_queue_on_every_process()
        {
            // Fixed values: the Api and the Worker must agree, so this must not change between runs.
            LogicConstants.NodeQueueIndex("exec-1").Should().Be(LogicConstants.NodeQueueIndex("exec-1"));
            LogicConstants.NodeQueueFor("abc").Should().Be(LogicConstants.AllNodeQueues[LogicConstants.NodeQueueIndex("abc")]);
            LogicConstants.NodeQueueIndex("a").Should().Be((int)(unchecked((2166136261u ^ 'a') * 16777619u) % 17));
        }

        [Fact]
        public void Runs_spread_over_all_queues()
        {
            var used = Enumerable.Range(0, 2000).Select(i => LogicConstants.NodeQueueIndex(Guid.NewGuid().ToString("N"))).ToHashSet();
            used.Should().HaveCount(17, "the old queue is used too");
        }

        [Fact]
        public async Task Steps_on_the_same_queue_run_one_at_a_time_and_other_queues_go_on()
        {
            var lanes = new NodeQueueLanes();
            var gate = new TaskCompletionSource();
            var running = 0;
            var maxSameLane = 0;

            async Task Step()
            {
                maxSameLane = Math.Max(maxSameLane, Interlocked.Increment(ref running));
                await gate.Task;
                Interlocked.Decrement(ref running);
            }

            var a = lanes.RunAsync("t1", "exec-1", Step);
            var b = lanes.RunAsync("t1", "exec-1", Step);       // same run → same queue → waits
            var otherId = Enumerable.Range(0, 100).Select(i => $"x{i}")
                .First(id => LogicConstants.NodeQueueIndex(id) != LogicConstants.NodeQueueIndex("exec-1"));
            var otherDone = lanes.RunAsync("t2", otherId, () => Task.CompletedTask);

            await otherDone.WaitAsync(TimeSpan.FromSeconds(5));   // another queue is not blocked
            await Task.Delay(50);
            running.Should().Be(1);

            gate.SetResult();
            await Task.WhenAll(a, b);
            maxSameLane.Should().Be(1);
        }

        [Fact]
        public async Task A_tenant_has_at_most_its_share_working_and_other_tenants_go_on()
        {
            var lanes = new NodeQueueLanes(tenantMax: 2);
            var gate = new TaskCompletionSource();
            var running = 0;
            var peak = 0;

            // Ids on three different queues, so only the tenant share can hold them back.
            var ids = Enumerable.Range(0, 200).Select(i => $"r{i}")
                .GroupBy(LogicConstants.NodeQueueIndex).Select(g => g.First()).Take(3).ToList();

            var tasks = ids.Select(id => lanes.RunAsync("t1", id, async () =>
            {
                peak = Math.Max(peak, Interlocked.Increment(ref running));
                await gate.Task;
                Interlocked.Decrement(ref running);
            })).ToList();

            var otherId = Enumerable.Range(0, 200).Select(i => $"o{i}")
                .First(id => !ids.Select(LogicConstants.NodeQueueIndex).Contains(LogicConstants.NodeQueueIndex(id)));
            await lanes.RunAsync("t2", otherId, () => Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));

            await Task.Delay(50);
            running.Should().Be(2, "t1's third step waits for a slot");
            gate.SetResult();
            await Task.WhenAll(tasks);
            peak.Should().Be(2);
        }
    }
}

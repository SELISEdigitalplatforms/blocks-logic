using Blocks.FunctionRunner.Runs;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Which stream the run loop reads: HTTP first, one event run after every five while both wait, every free slot to
    /// events when the runs stream is empty. Pure logic, no Redis.
    /// </summary>
    public sealed class RunSourceSelectorTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void The_rule_is_five_runs_per_event_run()
        {
            RunSourceSelector.HttpPerEvent.Should().Be(5);
        }

        [Fact]
        public void A_fresh_selector_takes_runs_first_and_is_not_due_for_an_event()
        {
            var selector = new RunSourceSelector();

            selector.EventDue(Now).Should().BeFalse();
            selector.RunsToRead(16, Now).Should().Be(5, "never past the next due event run while events may wait");
        }

        [Fact]
        public void After_five_runs_an_event_run_is_due_and_taking_it_starts_the_count_again()
        {
            var selector = new RunSourceSelector();
            selector.TookRuns(4);
            selector.EventDue(Now).Should().BeFalse();
            selector.RunsToRead(16, Now).Should().Be(1);

            selector.TookRuns(1);
            selector.EventDue(Now).Should().BeTrue();

            selector.TookEvents(1);
            selector.EventDue(Now).Should().BeFalse();
            selector.RunsSinceEvent.Should().Be(0);
        }

        [Fact]
        public void A_batch_never_reads_more_runs_than_are_left_before_the_next_event_run()
        {
            var selector = new RunSourceSelector();
            selector.TookRuns(3);

            selector.RunsToRead(room: 16, Now).Should().Be(2);
            selector.RunsToRead(room: 1, Now).Should().Be(1);
            selector.RunsToRead(room: 0, Now).Should().Be(0);
        }

        [Fact]
        public void When_the_event_stream_was_just_found_empty_runs_use_the_whole_room_and_no_event_read_is_due()
        {
            var selector = new RunSourceSelector();
            selector.TookRuns(5);
            selector.EventDue(Now).Should().BeTrue();

            selector.EventsEmpty(Now);

            selector.EventDue(Now).Should().BeFalse("no second round trip for an empty stream");
            selector.RunsToRead(16, Now).Should().Be(16, "HTTP alone uses every slot");
            selector.TookRuns(16);
            selector.EventDue(Now + RunSourceSelector.EmptyProbeInterval - TimeSpan.FromMilliseconds(1)).Should().BeFalse();
            selector.EventDue(Now + RunSourceSelector.EmptyProbeInterval).Should().BeTrue("probed again after the interval");
        }

        [Fact]
        public void A_nudge_or_new_events_end_the_empty_period_at_once()
        {
            var selector = new RunSourceSelector();
            selector.TookRuns(5);
            selector.EventsEmpty(Now);

            selector.EventsMayBeWaiting();

            selector.EventDue(Now).Should().BeTrue();
            selector.RunsToRead(16, Now).Should().Be(1, "with the counter at five, one run is the most before the event run");
        }

        [Fact]
        public void Taking_nothing_changes_nothing()
        {
            var selector = new RunSourceSelector();
            selector.TookRuns(3);

            selector.TookRuns(0);
            selector.TookEvents(0);

            selector.RunsSinceEvent.Should().Be(3);
        }

        [Fact]
        public void Simulated_with_both_queues_full_the_order_is_five_runs_then_one_event_run_over_and_over()
        {
            var selector = new RunSourceSelector();
            var order = new List<char>();
            for (var step = 0; step < 18; step++)
            {
                if (selector.EventDue(Now))
                {
                    order.Add('E');
                    selector.TookEvents(1);
                    continue;
                }
                var take = selector.RunsToRead(1, Now);
                order.AddRange(Enumerable.Repeat('H', take));
                selector.TookRuns(take);
            }

            new string([.. order]).Should().StartWith("HHHHHEHHHHHEHHHHHE");
        }
    }
}

using Blocks.FunctionRunner.Health;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Flood control. A host that stays over a limit for a day, or an error that repeats in a
    /// loop, must not turn the event files into the next thing that fills the disk.
    /// </summary>
    public sealed class HealthEventTests : IDisposable
    {
        private static readonly DateTimeOffset Noon = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        private static readonly Breach Memory = new("memory", "low", 5, 10);
        private static readonly Breach Disk = new("disk", "full", 90, 85);
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "fn-events-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* temp dir, best effort */ }
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void A_breach_is_raised_on_entry_then_only_as_a_reminder_every_ten_minutes()
        {
            var gate = new EventGate();

            gate.Admit([Memory], Noon).Should().ContainSingle();
            gate.Admit([Memory], Noon.AddMinutes(1)).Should().BeEmpty();
            gate.Admit([Memory], Noon.AddMinutes(9)).Should().BeEmpty();
            gate.Admit([Memory], Noon.AddMinutes(10)).Should().ContainSingle();
            gate.Admit([Memory], Noon.AddMinutes(11)).Should().BeEmpty();
        }

        [Fact]
        public void A_cleared_breach_is_raised_again_at_once_when_it_returns()
        {
            var gate = new EventGate();
            gate.Admit([Memory], Noon);
            gate.Admit([], Noon.AddMinutes(1));

            gate.Admit([Memory], Noon.AddMinutes(2)).Should().ContainSingle();
        }

        [Fact]
        public void Breaches_are_tracked_separately()
        {
            var gate = new EventGate();
            gate.Admit([Memory], Noon);

            gate.Admit([Memory, Disk], Noon.AddMinutes(1)).Should().ContainSingle().Which.Key.Should().Be("disk");
        }

        [Fact]
        public void Error_events_are_capped_per_minute_and_the_rest_summarised_once()
        {
            var clock = new ManualClock(Noon);
            var store = new MetricsStore(_dir, clock);
            var sink = new ErrorEventSink(store, clock);

            for (var i = 0; i < 100; i++) sink.Record(LogLevel.Error, "Cat", $"boom {i}", null);
            store.ReadEvents(1).Should().HaveCount(60);

            clock.Advance(TimeSpan.FromSeconds(61));
            sink.Flush();
            sink.Flush();

            var events = store.ReadEvents(1);
            events.Should().HaveCount(61);
            events.Single(e => e.Kind == "error-summary").Value.Should().Be(40);
        }

        [Fact]
        public void An_error_event_keeps_the_exception_type_and_message_but_no_stack()
        {
            var clock = new ManualClock(Noon);
            var store = new MetricsStore(_dir, clock);
            var sink = new ErrorEventSink(store, clock);
            Exception thrown;
            try { throw new InvalidOperationException("bad state"); }
            catch (InvalidOperationException ex) { thrown = ex; }

            sink.Record(LogLevel.Error, "Cat", "failed", thrown);

            var message = store.ReadEvents(1).Single().Message;
            message.Should().Contain("InvalidOperationException: bad state");
            message.Should().NotContain(" at ");
        }
    }
}

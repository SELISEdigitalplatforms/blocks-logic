namespace Blocks.FunctionRunner.Runs
{
    /// <summary>
    /// Which stream the run loop reads next: <c>functions:runs</c> (HTTP and the rest) or
    /// <c>functions:event-runs</c> (function events). The rule: HTTP first; while both wait, one event run after
    /// every <see cref="HttpPerEvent"/> runs taken from the runs stream; when the runs stream is empty, events use
    /// every free slot. State is per runner process, and only the run loop touches it.
    /// <para>
    /// The loop cannot know a stream is empty without reading it, so an event read that came back empty is
    /// remembered for <see cref="EmptyProbeInterval"/>: busy HTTP traffic does not pay a second Redis round trip
    /// on every turn for a stream that has nothing.
    /// </para>
    /// </summary>
    internal sealed class RunSourceSelector
    {
        /// <summary>Runs taken from the runs stream before one event run is due, while both queues wait.</summary>
        internal const int HttpPerEvent = 5;

        /// <summary>How long an empty event stream is left alone while HTTP runs keep the loop busy.</summary>
        internal static readonly TimeSpan EmptyProbeInterval = TimeSpan.FromMilliseconds(250);

        private int _runsSinceEvent;
        private DateTimeOffset _eventsEmptyUntil = DateTimeOffset.MinValue;

        /// <summary>Runs taken since the last event run (diagnostic and test seam).</summary>
        internal int RunsSinceEvent => _runsSinceEvent;

        /// <summary>
        /// True when an event run is due before more runs: <see cref="HttpPerEvent"/> runs went by and the event
        /// stream was not found empty a moment ago. The loop then reads one event entry first.
        /// </summary>
        public bool EventDue(DateTimeOffset now) =>
            _runsSinceEvent >= HttpPerEvent && now >= _eventsEmptyUntil;

        /// <summary>
        /// How many runs may be read now, at most <paramref name="room"/>: never past the next due event run
        /// while events may be waiting, so a batch cannot push the 1:5 rule off.
        /// </summary>
        public int RunsToRead(int room, DateTimeOffset now)
        {
            if (room <= 0) return 0;
            if (now < _eventsEmptyUntil) return room;
            return Math.Min(room, Math.Max(1, HttpPerEvent - _runsSinceEvent));
        }

        /// <summary>Entries were taken from the runs stream.</summary>
        public void TookRuns(int count)
        {
            if (count > 0) _runsSinceEvent += count;
        }

        /// <summary>Entries were taken from the event stream: the next event run is five runs away again.</summary>
        public void TookEvents(int count)
        {
            if (count > 0) _runsSinceEvent = 0;
        }

        /// <summary>A read of the event stream found nothing.</summary>
        public void EventsEmpty(DateTimeOffset now) => _eventsEmptyUntil = now + EmptyProbeInterval;

        /// <summary>Event entries were seen again (a nudge or a read): do not skip the next probe.</summary>
        public void EventsMayBeWaiting() => _eventsEmptyUntil = DateTimeOffset.MinValue;
    }
}

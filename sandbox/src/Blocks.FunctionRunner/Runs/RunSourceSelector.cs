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
    /// <para>
    /// Under load the share grows: when an event run taken had waited half its wait limit or more, the event
    /// queue is behind, and one event run is taken after every <see cref="HttpPerEventBehind"/> runs until an
    /// event run is taken within a quarter of the limit or the event stream is found empty. Only live event runs
    /// count as the event turn: an expired one is not started, so it does not move the next event run away.
    /// </para>
    /// </summary>
    internal sealed class RunSourceSelector
    {
        /// <summary>Runs taken from the runs stream before one event run is due, while both queues wait.</summary>
        internal const int HttpPerEvent = 5;

        /// <summary>The same while the event queue is behind (see <see cref="EventWaited"/>).</summary>
        internal const int HttpPerEventBehind = 2;

        /// <summary>How long an empty event stream is left alone while HTTP runs keep the loop busy.</summary>
        internal static readonly TimeSpan EmptyProbeInterval = TimeSpan.FromMilliseconds(250);

        private int _runsSinceEvent;
        private bool _behind;
        private DateTimeOffset _eventsEmptyUntil = DateTimeOffset.MinValue;

        /// <summary>Runs taken since the last event run (diagnostic and test seam).</summary>
        internal int RunsSinceEvent => _runsSinceEvent;

        /// <summary>True while the event queue is behind and events get the larger share.</summary>
        internal bool Behind => _behind;

        /// <summary>Runs before the next event run under the current share.</summary>
        internal int CurrentHttpPerEvent => _behind ? HttpPerEventBehind : HttpPerEvent;

        /// <summary>
        /// True when an event run is due before more runs: <see cref="CurrentHttpPerEvent"/> runs went by and the
        /// event stream was not found empty a moment ago. The loop then reads one event entry first.
        /// </summary>
        public bool EventDue(DateTimeOffset now) =>
            _runsSinceEvent >= CurrentHttpPerEvent && now >= _eventsEmptyUntil;

        /// <summary>
        /// How many runs may be read now, at most <paramref name="room"/>: never past the next due event run
        /// while events may be waiting, so a batch cannot push the rule off.
        /// </summary>
        public int RunsToRead(int room, DateTimeOffset now)
        {
            if (room <= 0) return 0;
            if (now < _eventsEmptyUntil) return room;
            return Math.Min(room, Math.Max(1, CurrentHttpPerEvent - _runsSinceEvent));
        }

        /// <summary>Entries were taken from the runs stream.</summary>
        public void TookRuns(int count)
        {
            if (count > 0) _runsSinceEvent += count;
        }

        /// <summary>
        /// Live event runs (not expired) were taken from the event stream: the next event run is a full share
        /// away again. Expired entries are not counted, so the turn stays due and the next entry is read.
        /// </summary>
        public void TookEvents(int liveCount)
        {
            if (liveCount > 0) _runsSinceEvent = 0;
        }

        /// <summary>
        /// The longest wait among the event entries just taken, against the wait limit: half the limit or more
        /// puts the queue behind; under a quarter puts it back. Between the two the state stays (no flapping).
        /// </summary>
        public void EventWaited(long waitedMs, long limitMs)
        {
            if (limitMs <= 0) return;
            if (waitedMs * 2 >= limitMs) _behind = true;
            else if (waitedMs * 4 < limitMs) _behind = false;
        }

        /// <summary>A read of the event stream found nothing: the queue is not behind.</summary>
        public void EventsEmpty(DateTimeOffset now)
        {
            _eventsEmptyUntil = now + EmptyProbeInterval;
            _behind = false;
        }

        /// <summary>Event entries were seen again (a nudge or a read): do not skip the next probe.</summary>
        public void EventsMayBeWaiting() => _eventsEmptyUntil = DateTimeOffset.MinValue;
    }
}

namespace Functions.DomainService.Queue
{
    /// <summary>
    /// Redis keys used only between this control plane's own hosts (Api and Worker) — unlike
    /// <see cref="FunctionQueueKeys"/>, none of these is part of the runner contract
    /// (plan/PROTOCOL.md), so they can change without touching the runner.
    /// </summary>
    public static class FunctionWorkerQueueKeys
    {
        /// <summary>
        /// Output-action jobs: one entry per (run, attempt) whose successful result has output
        /// actions to deliver. Written by <see cref="Consumers.FunctionResultConsumer"/> and read by
        /// <see cref="Consumers.FunctionOutputActionConsumer"/>, so a slow external endpoint never
        /// holds a result entry open long enough to be reclaimed and applied twice.
        /// </summary>
        public const string OutputsStream = "functions:outputs";

        /// <summary>
        /// The "this action has been dispatched" marker for one action of one run attempt — the
        /// idempotency key <c>runId + attempt + action</c>. Written with SET NX before the action
        /// is sent, so whichever Worker writes it first is the only one that ever sends it; a
        /// redelivered or reclaimed job finds it and reuses the recorded outcome instead.
        /// <paramref name="actionKey"/> is the action's position and id together, so two actions
        /// that happen to share an id can never suppress each other.
        /// </summary>
        public static string OutputActionMarker(string runId, int attempt, string actionKey)
            => $"function:output-action:{runId}:{attempt}:{actionKey}";

        /// <summary>
        /// Comfortably longer than any window in which a job for the same attempt can be
        /// delivered again: the working streams are trimmed at 12 h (FunctionStreamTrimmer).
        /// </summary>
        public static readonly TimeSpan OutputActionMarkerTtl = TimeSpan.FromHours(48);

        /// <summary>
        /// The "this event has been dispatched" marker, keyed on the event id (<c>{runId}-{attempt}-{n}</c>, made by
        /// the sandbox's runtime and checked by the runner to start with the run's own id). SET NX before the send,
        /// then the outcome is written onto it: the same rule as <see cref="OutputActionMarker"/>.
        /// </summary>
        public static string EventSentMarker(string eventId) => $"function:event-sent:{eventId}";

        /// <summary>Long enough for any redelivery of the job (the working streams are trimmed at 12 h).</summary>
        public static readonly TimeSpan EventSentMarkerTtl = TimeSpan.FromDays(7);

        /// <summary>
        /// What a run started by a function event needs to know later: the event's name and the depth of its chain
        /// (<c>{"eventId","name","depth"}</c>). Written before the run record exists; read by the dispatcher (the depth
        /// of the events this run sends) and when the run fails for good (the dead-letter entry). Kept longer than a
        /// run's payload (<see cref="FunctionQueueKeys.RunTtl"/>) so it is there as long as the run can still act.
        /// </summary>
        public static string EventRunInfo(string runId) => $"function:event-run:{runId}";

        public static readonly TimeSpan EventRunInfoTtl = TimeSpan.FromHours(24);

        /// <summary>
        /// Held while one Worker starts the run of an event for a function, so a duplicate delivery of the same event
        /// at the same moment starts no second run. A short lock; the run record (found by the event id) is what
        /// answers later duplicates.
        /// </summary>
        public static string EventStartLock(string eventId, string functionId) => $"function:event-start:{eventId}:{functionId}";

        public static readonly TimeSpan EventStartLockTtl = TimeSpan.FromSeconds(60);

        /// <summary>Events sent by one tenant in one minute (<paramref name="minute"/> = unix time / 60).</summary>
        public static string EventRate(string tenantId, long minute) => $"function:events-rate:{tenantId}:{minute}";

        /// <summary>
        /// Written after a result's logs have been copied to Mongo for one attempt, so a
        /// redelivered result entry does not insert every log line a second time.
        /// </summary>
        public static string LogsCopied(string runId, int attempt) => $"function:logs-copied:{runId}:{attempt}";

        public static readonly TimeSpan LogsCopiedTtl = TimeSpan.FromHours(24);

        /// <summary>
        /// Held for one sweep interval by whichever Worker instance runs the stale-run sweep, so
        /// several instances do not all walk every tenant database on the same tick. Correctness
        /// never depends on it — every write the sweep makes is conditional.
        /// </summary>
        public const string StaleSweepLock = "functions:stale-sweep:lock";

        /// <summary>
        /// Set of deletes the Api has accepted and the Worker has not finished, one member per
        /// function (<see cref="PendingDeletionMember"/>). Only a fast path: the tombstone in Mongo
        /// is the record, and the Worker's backstop sweep re-adds any member this set loses.
        /// </summary>
        public const string PendingDeletions = "functions:deletions";

        public static string PendingDeletionMember(string tenantId, string functionId) => $"{tenantId}|{functionId}";

        /// <summary>
        /// Parses a <see cref="PendingDeletions"/> member. Refuses anything that is not exactly two
        /// non-empty parts, so a malformed member is dropped rather than purging a guessed id.
        /// </summary>
        public static bool TryParsePendingDeletion(string? member, out string tenantId, out string functionId)
        {
            tenantId = string.Empty;
            functionId = string.Empty;
            var parts = (member ?? string.Empty).Split('|');
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1])) return false;
            tenantId = parts[0];
            functionId = parts[1];
            return true;
        }

        /// <summary>Held while one Worker runs a purge pass for one function.</summary>
        public static string DeletionLock(string tenantId, string functionId) => $"functions:deletion-lock:{tenantId}:{functionId}";

        /// <summary>Held by whichever Worker runs the tombstone backstop sweep over every tenant.</summary>
        public const string DeletionSweepLock = "functions:deletion-sweep:lock";
    }
}

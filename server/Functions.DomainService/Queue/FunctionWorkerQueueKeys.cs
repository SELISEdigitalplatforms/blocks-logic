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
    }
}

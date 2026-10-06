using Functions.DomainService.Enums;

namespace Functions.DomainService.Queue
{
    /// <summary>
    /// Translates between the runner's wire strings and this half's enums.
    /// <para>
    /// Both directions are explicit and exhaustive on purpose. An unrecognised status must not
    /// silently become <see cref="RunStatus.Queued"/> — that would make a finished run look
    /// like a pending one forever — so it maps to <see cref="RunStatus.Failed"/> and the caller
    /// is told the value was unknown.
    /// </para>
    /// </summary>
    public static class FunctionWireMapping
    {
        /// <summary>Parses a wire status. <paramref name="recognised"/> is false for anything unexpected.</summary>
        public static RunStatus ToRunStatus(string? wire, out bool recognised)
        {
            recognised = true;
            switch (wire)
            {
                case FunctionQueueKeys.Wire.Queued: return RunStatus.Queued;
                case FunctionQueueKeys.Wire.Claimed: return RunStatus.Claimed;
                case FunctionQueueKeys.Wire.Starting: return RunStatus.Starting;
                case FunctionQueueKeys.Wire.Running: return RunStatus.Running;
                case FunctionQueueKeys.Wire.OutputProcessing: return RunStatus.OutputProcessing;
                case FunctionQueueKeys.Wire.Succeeded: return RunStatus.Succeeded;
                case FunctionQueueKeys.Wire.Failed: return RunStatus.Failed;
                case FunctionQueueKeys.Wire.TimedOut: return RunStatus.TimedOut;
                case FunctionQueueKeys.Wire.Cancelled: return RunStatus.Cancelled;
                case FunctionQueueKeys.Wire.ResourceExceeded: return RunStatus.ResourceExceeded;
                case FunctionQueueKeys.Wire.OutputFailed: return RunStatus.OutputFailed;
                default:
                    recognised = false;
                    return RunStatus.Failed;
            }
        }

        public static string ToWire(RunStatus status) => status switch
        {
            RunStatus.Queued => FunctionQueueKeys.Wire.Queued,
            RunStatus.Claimed => FunctionQueueKeys.Wire.Claimed,
            RunStatus.Starting => FunctionQueueKeys.Wire.Starting,
            RunStatus.Running => FunctionQueueKeys.Wire.Running,
            RunStatus.OutputProcessing => FunctionQueueKeys.Wire.OutputProcessing,
            RunStatus.Succeeded => FunctionQueueKeys.Wire.Succeeded,
            RunStatus.Failed => FunctionQueueKeys.Wire.Failed,
            RunStatus.TimedOut => FunctionQueueKeys.Wire.TimedOut,
            RunStatus.Cancelled => FunctionQueueKeys.Wire.Cancelled,
            RunStatus.ResourceExceeded => FunctionQueueKeys.Wire.ResourceExceeded,
            RunStatus.OutputFailed => FunctionQueueKeys.Wire.OutputFailed,
            _ => FunctionQueueKeys.Wire.Failed,
        };

        /// <summary>Parses a wire error code. Unknown codes become a user runtime error.</summary>
        public static RunErrorCode ToErrorCode(string? wire) => wire switch
        {
            null or "" => RunErrorCode.None,
            FunctionQueueKeys.Wire.MemoryLimit => RunErrorCode.MemoryLimit,
            FunctionQueueKeys.Wire.PidLimit => RunErrorCode.PidLimit,
            FunctionQueueKeys.Wire.UserRuntimeError => RunErrorCode.UserRuntimeError,
            FunctionQueueKeys.Wire.RuntimeStartFailed => RunErrorCode.RuntimeStartFailed,
            FunctionQueueKeys.Wire.ResultTooLarge => RunErrorCode.ResultTooLarge,
            FunctionQueueKeys.Wire.ResultNotSerializable => RunErrorCode.ResultNotSerializable,
            FunctionQueueKeys.Wire.ImagePullFailed => RunErrorCode.ImagePullFailed,
            FunctionQueueKeys.Wire.TimedOutCode => RunErrorCode.TimedOut,
            FunctionQueueKeys.Wire.SandboxStartFailed => RunErrorCode.SandboxStartFailed,
            FunctionQueueKeys.Wire.BuildFailedCode => RunErrorCode.BuildFailed,
            FunctionQueueKeys.Wire.SecretUnresolved => RunErrorCode.SecretUnresolved,
            FunctionQueueKeys.Wire.SecretStoreUnavailable => RunErrorCode.SecretStoreUnavailable,
            FunctionQueueKeys.Wire.Undeliverable => RunErrorCode.Undeliverable,
            _ => RunErrorCode.UserRuntimeError,
        };

        /// <summary>True once no further transition is possible.</summary>
        public static bool IsTerminal(RunStatus status) => status is
            RunStatus.Succeeded or RunStatus.Failed or RunStatus.TimedOut or
            RunStatus.Cancelled or RunStatus.ResourceExceeded or RunStatus.OutputFailed;

        /// <summary>
        /// Outcomes the platform recorded itself (never ran, or never answered). A real result may
        /// still replace them, so they are never the trigger for a retry of their own.
        /// </summary>
        public static bool IsPlatformDetermined(RunErrorCode code) =>
            code is RunErrorCode.Undeliverable or RunErrorCode.EnqueueFailed or RunErrorCode.Abandoned;

        /// <summary>
        /// The result consumer's test for "this finished run is one a retry may be scheduled for":
        /// terminal, reported by a runner (not platform-determined) and of a retryable kind. The
        /// consumer then still checks the attempt budget and the version (<see cref="WillBeRetried"/>).
        /// </summary>
        public static bool IsRetryCandidate(Entities.FunctionRunEntity run) =>
            IsTerminal(run.Status) && !IsPlatformDetermined(run.ErrorCode) && IsRetryable(run.Status, run.ErrorCode);

        /// <summary>
        /// True when the result consumer will schedule another attempt for this finished run: a
        /// <see cref="IsRetryCandidate"/> of a deployed version with attempts left. The same
        /// predicate the consumer applies, so a synchronous HTTP caller is never handed a failure
        /// that a retry is about to replace.
        /// </summary>
        public static bool WillBeRetried(Entities.FunctionRunEntity run) =>
            IsRetryCandidate(run) && !string.IsNullOrEmpty(run.VersionId) && run.Attempt < run.MaxAttempts;

        /// <summary>
        /// True when a failed run is worth retrying. A run that broke because the tenant's code
        /// threw, or returned something unserializable, will do exactly the same thing again —
        /// only infrastructure and resource failures are worth another attempt.
        /// </summary>
        public static bool IsRetryable(RunStatus status, RunErrorCode code)
        {
            if (status is RunStatus.Cancelled or RunStatus.Succeeded) return false;

            return code switch
            {
                RunErrorCode.UserRuntimeError => false,
                RunErrorCode.ResultTooLarge => false,
                RunErrorCode.ResultNotSerializable => false,
                // The job was dead-lettered, which only happens once its delivery budget is
                // already spent. Scheduling another attempt would re-enqueue work that the
                // runners have collectively refused, and it would do so on a timer.
                RunErrorCode.Undeliverable => false,
                // A reference to a secret that does not exist or cannot be read fails the same way
                // every time until the author fixes the variable. (SecretStoreUnavailable — the
                // store itself was down — falls through to retryable.)
                RunErrorCode.SecretUnresolved => false,
                RunErrorCode.BuildFailed => false,
                _ => true,
            };
        }
    }
}

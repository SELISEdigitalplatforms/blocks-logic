using Blocks.FunctionRunner.Contracts;

namespace Blocks.FunctionRunner.Protocol
{
    /// <summary>How a finished sandbox is turned into a status and error code.</summary>
    public static class RunOutcome
    {
        /// <summary>
        /// Maps the container's post-mortem and its parsed output onto the wire status.
        /// <para>
        /// Order matters and is deliberate. The container's own fate outranks anything it
        /// printed: a sandbox that was OOM-killed reports RESOURCE_EXCEEDED even if it managed
        /// to emit a cheerful result line first, because the kill is the fact and the line is a
        /// claim.
        /// </para>
        /// </summary>
        /// <param name="oomKilled">Docker's OOMKilled flag from inspect.</param>
        /// <param name="exitCode">The container exit code.</param>
        /// <param name="timedOut">True when the runner's hard deadline fired.</param>
        /// <param name="cancelled">True when a cancel request was seen.</param>
        /// <param name="output">The parsed stdout, which may be empty.</param>
        public static (string Status, string? ErrorCode, string? ErrorMessage) Map(
            bool oomKilled,
            int exitCode,
            bool timedOut,
            bool cancelled,
            SandboxOutput output)
        {
            ArgumentNullException.ThrowIfNull(output);

            if (oomKilled)
            {
                return (RunStatuses.ResourceExceeded, ErrorCodes.MemoryLimit,
                    "the function exceeded its memory limit and was terminated");
            }

            if (cancelled)
            {
                return (RunStatuses.Cancelled, null, "the run was cancelled");
            }

            if (timedOut)
            {
                return (RunStatuses.TimedOut, ErrorCodes.TimedOut,
                    "the function exceeded its time limit and was terminated");
            }

            // The sandbox's own verdict, when it produced one.
            if (output.Ok == true)
            {
                return (RunStatuses.Succeeded, null, null);
            }

            if (output.Ok == false)
            {
                var status = output.ErrorCode switch
                {
                    ErrorCodes.TimedOut => RunStatuses.TimedOut,
                    ErrorCodes.MemoryLimit or ErrorCodes.PidLimit => RunStatuses.ResourceExceeded,
                    _ => RunStatuses.Failed,
                };
                return (status, output.ErrorCode, output.ErrorMessage);
            }

            // No result line at all: fall back to the exit code.
            return exitCode switch
            {
                SandboxExit.Ok => (RunStatuses.Failed, ErrorCodes.RuntimeStartFailed,
                    "the sandbox exited successfully without returning a result"),
                SandboxExit.BootstrapError => (RunStatuses.Failed, ErrorCodes.RuntimeStartFailed,
                    "the runtime failed to start the function"),
                SandboxExit.Sigkill => (RunStatuses.ResourceExceeded, ErrorCodes.MemoryLimit,
                    "the sandbox was killed; the most likely cause is the memory ceiling"),
                SandboxExit.Sigterm => (RunStatuses.Cancelled, null,
                    "the sandbox was terminated by the runner"),
                _ => (RunStatuses.Failed, ErrorCodes.UserRuntimeError,
                    $"the sandbox exited with code {exitCode} without returning a result"),
            };
        }
    }
}

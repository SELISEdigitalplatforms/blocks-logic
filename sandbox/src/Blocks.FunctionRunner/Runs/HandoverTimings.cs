using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Blocks.FunctionRunner.Runs
{
    /// <summary>
    /// Where a run's hand-over went — claim to envelope on the sandbox's stdin — split by step, so
    /// the slow one is named in the log rather than guessed (a warm call measured ~1.7 s of
    /// hand-over against ~0.2 s of the function's own code, 2026-10-06).
    /// <para>
    /// Marks are milliseconds since the claim; -1 is "not reached". The secret and token times are
    /// durations, recorded from whichever thread did the work, because the two run side by side.
    /// No values, only times — this goes to the runner's log.
    /// </para>
    /// </summary>
    public sealed class HandoverTimings
    {
        private readonly Stopwatch _claimed;
        private long _image = -1, _admitted = -1, _sandbox = -1, _secrets = -1, _token = -1;

        public HandoverTimings(Stopwatch claimed) => _claimed = claimed;

        public void ImageReady() => Volatile.Write(ref _image, _claimed.ElapsedMilliseconds);
        public void Admitted() => Volatile.Write(ref _admitted, _claimed.ElapsedMilliseconds);
        public void SandboxReady() => Volatile.Write(ref _sandbox, _claimed.ElapsedMilliseconds);
        public void Secrets(long durationMs) => Volatile.Write(ref _secrets, durationMs);
        public void Token(long durationMs) => Volatile.Write(ref _token, durationMs);

        /// <summary>One line per warm call. Each step is the time since the previous mark.</summary>
        public void Log(ILogger logger, string runId, long? handoverMs, bool reused)
        {
            long image = Volatile.Read(ref _image), admitted = Volatile.Read(ref _admitted),
                sandbox = Volatile.Read(ref _sandbox);
            logger.LogInformation(
                "Run {RunId} handover {HandoverMs} ms (reused {Reused}): image {ImageMs} ms, admission {AdmissionMs} ms, "
                + "sandbox {SandboxMs} ms, secrets {SecretsMs} ms, token {TokenMs} ms",
                runId, handoverMs ?? -1, reused,
                image, Step(image, admitted), Step(admitted, sandbox),
                Volatile.Read(ref _secrets), Volatile.Read(ref _token));
        }

        private static long Step(long from, long to) => from < 0 || to < 0 ? -1 : to - from;
    }
}

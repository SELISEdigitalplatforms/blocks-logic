namespace Functions.DomainService.Utils
{
    /// <summary>
    /// The platform could not accept the invocation right now — the run queue (Redis) refused or
    /// timed out — and nothing was executed. Maps to <b>503</b> with a <c>Retry-After</c> of
    /// <see cref="RetryAfterSeconds"/>: the caller did nothing wrong and the same request is
    /// expected to succeed shortly, which is exactly what 503 (not 500, not 400) tells a client.
    /// <para>
    /// <see cref="RunId"/> names the run record that was written and then closed as
    /// FAILED / <c>EnqueueFailed</c>, so the failure is visible in the runs list rather than
    /// only in the caller's error; it is null when the failure happened before any record existed.
    /// </para>
    /// </summary>
    public class FunctionUnavailableException(string message, string? runId = null, int retryAfterSeconds = 5)
        : Exception(message)
    {
        public string? RunId { get; } = runId;

        public int RetryAfterSeconds { get; } = retryAfterSeconds;
    }
}

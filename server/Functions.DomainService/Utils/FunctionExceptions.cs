namespace Functions.DomainService.Utils
{
    /// <summary>
    /// A request failed validation the caller can fix (bad input, an unknown reference). Maps
    /// to 400 at the controller boundary.
    /// </summary>
    public class FunctionValidationException(string message) : Exception(message);

    /// <summary>
    /// A deploy whose build already failed. Still a 400, plus the build's id so the editor can show
    /// that build's log, not only its one-line reason (PKG-14).
    /// </summary>
    public class FunctionBuildFailedException(string message, string buildId) : FunctionValidationException(message)
    {
        public string BuildId { get; } = buildId;
    }

    /// <summary>The requested function, version, run or build does not exist. Maps to 404.</summary>
    public class FunctionNotFoundException(string message) : Exception(message);

    /// <summary>
    /// The caller presented no usable credentials for a function that requires them — no tenant,
    /// no token, or a token that did not validate. Maps to 401.
    /// </summary>
    public class FunctionAuthorizationException(string message) : Exception(message);

    /// <summary>
    /// The caller authenticated but fails the trigger's role / permission rules, or the trigger
    /// does not accept this kind of invocation at all. Maps to 403 — distinct from 401 so a client
    /// knows that presenting the same token again will not help.
    /// </summary>
    public class FunctionForbiddenException(string message) : Exception(message);

    /// <summary>
    /// The call used a method the trigger does not answer. Maps to 405, with <see cref="Allowed"/>
    /// as the <c>Allow</c> header — the one method the function does accept.
    /// </summary>
    public class FunctionMethodNotAllowedException(string allowed)
        : Exception($"this function answers {allowed} only")
    {
        public string Allowed { get; } = allowed;
    }

    /// <summary>The request body is over the sandbox input ceiling. Maps to 413.</summary>
    public class FunctionRequestTooLargeException(string message) : Exception(message);

    /// <summary>
    /// The function's own requests-per-minute or requests-per-day limit refused the call (only
    /// reachable with <c>Functions:RateLimits:Enabled</c>). Maps to 429, with
    /// <see cref="RetryAfterSeconds"/> as the <c>Retry-After</c> header — the seconds until the
    /// window that refused it rolls over. Deliberately not a <see cref="FunctionValidationException"/>:
    /// the request was valid, and a client that sees 400 does not retry.
    /// </summary>
    public class FunctionRateLimitedException(string message, int retryAfterSeconds) : Exception(message)
    {
        public int RetryAfterSeconds { get; } = Math.Max(1, retryAfterSeconds);
    }
}

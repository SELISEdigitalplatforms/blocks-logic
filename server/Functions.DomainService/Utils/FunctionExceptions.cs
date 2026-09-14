namespace Functions.DomainService.Utils
{
    /// <summary>
    /// A request failed validation the caller can fix (bad input, an unknown reference). Maps
    /// to 400 at the controller boundary.
    /// </summary>
    public class FunctionValidationException(string message) : Exception(message);

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
}

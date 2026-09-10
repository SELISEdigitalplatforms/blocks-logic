namespace Functions.DomainService.Utils
{
    /// <summary>
    /// A request failed validation the caller can fix (bad input, an unknown reference). Maps
    /// to 400 at the controller boundary.
    /// </summary>
    public class FunctionValidationException(string message) : Exception(message);

    /// <summary>The requested function, version, run or build does not exist. Maps to 404.</summary>
    public class FunctionNotFoundException(string message) : Exception(message);

    /// <summary>The caller is not authorized to invoke this function. Maps to 401/403.</summary>
    public class FunctionAuthorizationException(string message) : Exception(message);
}

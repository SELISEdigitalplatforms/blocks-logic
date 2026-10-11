using Blocks.FunctionRunner.Runs;

namespace Blocks.FunctionRunner.SecretStore
{
    /// <summary>
    /// Resolves the secret ids one run references, for that run's tenant.
    /// <para>
    /// The contract separates the two ways a lookup can go wrong, because they need opposite
    /// handling. A reference that cannot be satisfied — no such secret, deleted, locked, no value,
    /// not readable in this context — comes back in <see cref="SecretLookup.Unresolved"/> and
    /// fails the run for good: running it again changes nothing. A store that could not be asked
    /// at all throws <see cref="SecretStoreUnavailableException"/>, and the run is reported as a
    /// retryable platform failure.
    /// </para>
    /// <para>
    /// Values are returned to the caller and nowhere else: an implementation never logs one, and
    /// never lets one into an exception message.
    /// </para>
    /// </summary>
    public interface IRunSecretResolver
    {
        Task<SecretLookup> ResolveAsync(
            string tenantId,
            EnvSecretReferences.Caller caller,
            IReadOnlyCollection<string> secretIds,
            CancellationToken cancellationToken);
    }

    /// <summary>The outcome of one lookup.</summary>
    /// <param name="Values">Id &rarr; plaintext for every id that resolved.</param>
    /// <param name="Unresolved">
    /// Id &rarr; a short, value-free reason (<see cref="SecretUnresolvedReasons"/>) for every id
    /// that did not.
    /// </param>
    public sealed record SecretLookup(
        IReadOnlyDictionary<string, string> Values,
        IReadOnlyDictionary<string, string> Unresolved);

    /// <summary>Why an id did not resolve. Shown to the function's author, so plain words.</summary>
    public static class SecretUnresolvedReasons
    {
        public const string NotFound = "it does not exist";
        public const string NoValue = "it has no value";
        public const string Locked = "it is locked";
        public const string Deleted = "it has been deleted";
        public const string AccessDenied = "it is not readable in this run's context";
        public const string UnknownTenant = "the run's tenant is not known to the secret store";
        public const string Invalid = "the reference is not a valid secret id";
    }

    /// <summary>
    /// The secret store could not be reached, or did not answer in time. Retryable. The message
    /// is the runner's own wording and never carries a value.
    /// </summary>
    public sealed class SecretStoreUnavailableException(string message, Exception? inner = null)
        : Exception(message, inner);
}

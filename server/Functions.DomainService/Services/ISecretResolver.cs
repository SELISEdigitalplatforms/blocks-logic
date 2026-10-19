namespace Functions.DomainService.Services
{
    /// <summary>
    /// Resolves secret <b>ids</b> to their plaintext values for one tenant.
    /// <para>
    /// Ids, never names: an output action template stores <c>{{secret.&lt;secretId&gt;}}</c>,
    /// not <c>{{secret.&lt;name&gt;}}</c>, so renaming a secret in the catalog never breaks a
    /// template that already references it — the SDK's own guidance is "store the id, never
    /// the value", and an id is exactly as stable a reference as a value would be unstable.
    /// </para>
    /// <para>
    /// Values returned here are used exactly once, in-memory, to build an outbound HTTP
    /// request in <see cref="OutputActionProcessor"/>. They are never logged, never persisted,
    /// and never reach the sandbox — the sandbox only ever sees the secret's <i>name</i>,
    /// through the picker in the editor, never its value.
    /// </para>
    /// </summary>
    public interface ISecretResolver
    {
        /// <summary>
        /// Resolves every id in <paramref name="secretIds"/> that this tenant can read. An id
        /// that does not exist, or that the caller cannot read, is simply absent from the
        /// result rather than raising — a stale reference in a template must not take down
        /// every output action, only leave that one placeholder unresolved (which the
        /// processor then reports as a distinct, attributable failure).
        /// </summary>
        Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            IReadOnlyCollection<string> secretIds, string tenantId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Every secret id whose name is <b>exactly</b> (case-sensitive) each of <paramref name="names"/> — for an
        /// output action that still carries an imported <c>{{secretName.&lt;name&gt;}}</c> (2026-10-18). Metadata only:
        /// no value is read here, and nothing is cached. A name with no match maps to an empty list. Throws when the
        /// store cannot be asked. The default knows no names, so such an action fails clearly instead of sending the
        /// reference text.
        /// </summary>
        Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> FindIdsByNameAsync(
            IReadOnlyCollection<string> names, string tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
                names.Distinct(StringComparer.Ordinal).ToDictionary(n => n, _ => (IReadOnlyList<string>)[], StringComparer.Ordinal));
    }
}

using System.Text.RegularExpressions;
using Functions.DomainService.Models;
using Proxy.DomainService.Utils;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// Refuses a typed (literal) credential in a function's configuration. User rule 2026-10-08: "never
    /// store a secret value"; store a reference, resolve it at use.
    /// <para>
    /// A variable (<c>ctx.env.KEY</c>) or an output-action header whose <b>name</b> looks like a credential
    /// (<see cref="ProxySecretRedactor.IsSensitiveKey"/>: <c>API_KEY</c>, <c>Authorization</c>, <c>DB_PASSWORD</c>,
    /// <c>X-Session</c>, …) must hold a <c>{{secret.&lt;id&gt;}}</c> reference. A scheme word around it is fine
    /// (<c>Bearer {{secret.x}}</c>); any other typed text is refused. An empty value holds nothing and passes.
    /// </para>
    /// <para>
    /// <b>Only new or changed values are refused.</b> A function saved before this rule keeps loading, running
    /// and saving unchanged: a value is accepted when the stored function already has the same name with the
    /// exact same value. Nothing new is stored by accepting it, and refusing it would block every other edit.
    /// </para>
    /// <para>Messages name the variable or header, never the value.</para>
    /// </summary>
    public static partial class FunctionSecretLiterals
    {
        // Same shape as OutputActionProcessor / FunctionEnvelopeBuilder: ids, word characters and dash.
        [GeneratedRegex(@"\{\{secret\.[\w-]+\}\}", RegexOptions.None, 1000)]
        private static partial Regex SecretReference();

        private static readonly string[] SchemeWords = ["Bearer", "Basic", "Token"];

        /// <summary>Whether a value under a credential-looking name would store a typed secret.</summary>
        public static bool HasLiteral(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string rest;
            try
            {
                rest = SecretReference().Replace(value, string.Empty).Trim();
            }
            catch (RegexMatchTimeoutException)
            {
                return true; // cannot tell: refuse
            }
            if (rest.Length == 0) return false;
            // "Bearer {{secret.x}}": the scheme word is not a secret.
            return !SchemeWords.Any(w => rest.Equals(w, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The errors for every new or changed literal credential in <paramref name="variables"/> and
        /// <paramref name="actions"/>, compared with what the function already stores. Empty = allowed.
        /// </summary>
        public static IReadOnlyList<string> FindNewLiterals(
            IEnumerable<VariableBinding>? variables,
            IEnumerable<OutputAction>? actions,
            IEnumerable<VariableBinding>? storedVariables,
            IEnumerable<OutputAction>? storedActions)
        {
            var errors = new List<string>();

            var storedVars = new HashSet<(string, string)>(
                (storedVariables ?? []).Where(v => v is not null).Select(v => (v.Key ?? string.Empty, v.Value ?? string.Empty)));
            foreach (var variable in variables ?? [])
            {
                if (variable is null || !ProxySecretRedactor.IsSensitiveKey(variable.Key)) continue;
                if (!HasLiteral(variable.Value)) continue;
                if (storedVars.Contains((variable.Key, variable.Value))) continue;
                errors.Add(
                    $"Variable \"{variable.Key}\" looks like a credential, so it cannot hold a typed value. " +
                    "Use a configuration variable instead.");
            }

            var storedHeaders = new HashSet<(string, string)>(
                (storedActions ?? [])
                    .Where(a => a?.Headers is not null)
                    .SelectMany(a => a.Headers)
                    .Select(h => (h.Key.ToLowerInvariant(), h.Value ?? string.Empty)));
            var index = 0;
            foreach (var action in actions ?? [])
            {
                index++;
                if (action?.Headers is null) continue;
                foreach (var header in action.Headers)
                {
                    if (!ProxySecretRedactor.IsSensitiveKey(header.Key)) continue;
                    if (!HasLiteral(header.Value)) continue;
                    if (storedHeaders.Contains((header.Key.ToLowerInvariant(), header.Value ?? string.Empty))) continue;
                    errors.Add(
                        $"Output action {index}: header \"{header.Key}\" looks like a credential, so it cannot hold a " +
                        "typed value. Use a configuration variable instead.");
                }
            }

            return errors;
        }
    }
}

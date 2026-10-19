using System.Text.RegularExpressions;
using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// The three ways a function's configuration can point at a secret, and nothing else:
    /// <list type="bullet">
    /// <item><c>{{secret.&lt;id&gt;}}</c> — the normal form. Written by the editor's picker, stable across a rename.</item>
    /// <item><c>{{secretName.&lt;name&gt;}}</c> — an <b>unbound</b> reference by exact name (approved 2026-10-18 for
    /// export/import). An import stores it when the target environment has no secret, or more than one, with that
    /// name; the runner and the output-action processor look the name up at run start, so creating the secret is
    /// the only step left. Never a value.</item>
    /// <item><c>{{secretUnresolved}}</c> — an export-file marker for a <c>{{secret.&lt;id&gt;}}</c> whose secret did not
    /// exist in the source environment. It names nothing, so nothing can resolve it.</item>
    /// </list>
    /// Functions-only (2026-10-07: Proxy and Functions do not share this kind of code).
    /// </summary>
    public static partial class FunctionSecretRefs
    {
        /// <summary>Same shape as the envelope builder, the output-action processor and the runner.</summary>
        [GeneratedRegex(@"\{\{secret\.([\w-]+)\}\}", RegexOptions.None, 1000)]
        public static partial Regex IdPattern();

        /// <summary>
        /// A Blocks OS secret name (<c>^[A-Za-z0-9][A-Za-z0-9._-]*$</c>, at most 100 characters,
        /// blocks-os <c>SecretService.Helpers.NamePattern</c>). The runner's <c>EnvSecretReferences</c> has the same pattern.
        /// </summary>
        [GeneratedRegex(@"\{\{secretName\.([A-Za-z0-9][A-Za-z0-9._-]{0,99})\}\}", RegexOptions.None, 1000)]
        public static partial Regex NamePattern();

        /// <summary>The export marker for a reference whose secret did not exist where the file was made.</summary>
        public const string UnresolvedMarker = "{{secretUnresolved}}";

        public static string IdToken(string id) => "{{secret." + id + "}}";

        public static string NameToken(string name) => "{{secretName." + name + "}}";

        /// <summary>Whether <paramref name="name"/> is a name a <c>{{secretName.…}}</c> token can carry.</summary>
        public static bool IsValidName(string? name) => !string.IsNullOrEmpty(name) && BareName().IsMatch(name);

        [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.None, 1000)]
        private static partial Regex BareName();

        public static IEnumerable<string> IdsIn(string? text) =>
            string.IsNullOrEmpty(text) ? [] : IdPattern().Matches(text).Select(m => m.Groups[1].Value);

        public static IEnumerable<string> NamesIn(string? text) =>
            string.IsNullOrEmpty(text) ? [] : NamePattern().Matches(text).Select(m => m.Groups[1].Value);

        public static string ReplaceIds(string? text, Func<string, string> replace) =>
            string.IsNullOrEmpty(text) ? text ?? string.Empty : IdPattern().Replace(text, m => replace(m.Groups[1].Value));

        public static string ReplaceNames(string? text, Func<string, string> replace) =>
            string.IsNullOrEmpty(text) ? text ?? string.Empty : NamePattern().Replace(text, m => replace(m.Groups[1].Value));

        /// <summary>
        /// Every place in a function's configuration a reference can sit, with a label saying where
        /// (<c>variable:KEY</c>, <c>outputAction:{id}:header:{name}</c>, <c>…:url</c>, <c>…:body</c>). Labels only — the
        /// values are returned for scanning by the caller and never logged.
        /// </summary>
        public static IEnumerable<(string Where, string Value)> Locations(
            IEnumerable<VariableBinding>? variables, IEnumerable<OutputAction>? actions)
        {
            foreach (var variable in variables ?? [])
            {
                if (variable is null || string.IsNullOrEmpty(variable.Value)) continue;
                yield return ($"variable:{variable.Key}", variable.Value);
            }
            foreach (var action in actions ?? [])
            {
                if (action is null) continue;
                if (!string.IsNullOrEmpty(action.Url)) yield return ($"outputAction:{action.Id}:url", action.Url);
                foreach (var header in action.Headers ?? [])
                {
                    if (!string.IsNullOrEmpty(header.Value)) yield return ($"outputAction:{action.Id}:header:{header.Key}", header.Value);
                }
                if (!string.IsNullOrEmpty(action.BodyTemplate)) yield return ($"outputAction:{action.Id}:body", action.BodyTemplate);
            }
        }

        /// <summary>The distinct <c>{{secretName.…}}</c> names a configuration uses, in first-seen order.</summary>
        public static List<string> NamesIn(IEnumerable<VariableBinding>? variables, IEnumerable<OutputAction>? actions) =>
            Locations(variables, actions).SelectMany(l => NamesIn(l.Value)).Distinct(StringComparer.Ordinal).ToList();

        /// <summary>The distinct <c>{{secret.…}}</c> ids a configuration uses, in first-seen order.</summary>
        public static List<string> IdsIn(IEnumerable<VariableBinding>? variables, IEnumerable<OutputAction>? actions) =>
            Locations(variables, actions).SelectMany(l => IdsIn(l.Value)).Distinct(StringComparer.Ordinal).ToList();

        /// <summary>
        /// A deep copy of the variables and output actions with <paramref name="rewrite"/> applied to every value that can
        /// hold a reference. The originals are not touched.
        /// </summary>
        public static (List<VariableBinding> Variables, List<OutputAction> Actions) Rewrite(
            IEnumerable<VariableBinding>? variables, IEnumerable<OutputAction>? actions, Func<string, string> rewrite)
        {
            var vars = (variables ?? [])
                .Where(v => v is not null)
                .Select(v => new VariableBinding { Key = v.Key, Value = rewrite(v.Value ?? string.Empty) })
                .ToList();
            var acts = (actions ?? [])
                .Where(a => a is not null)
                .Select(a => new OutputAction
                {
                    Id = a.Id,
                    Kind = a.Kind,
                    Enabled = a.Enabled,
                    Url = rewrite(a.Url ?? string.Empty),
                    Method = a.Method,
                    Headers = (a.Headers ?? []).ToDictionary(h => h.Key, h => rewrite(h.Value ?? string.Empty)),
                    BodyTemplate = a.BodyTemplate is null ? null : rewrite(a.BodyTemplate),
                    TimeoutSeconds = a.TimeoutSeconds,
                })
                .ToList();
            return (vars, acts);
        }
    }
}

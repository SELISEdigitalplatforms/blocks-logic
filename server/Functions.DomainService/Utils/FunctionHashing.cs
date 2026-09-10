using System.Security.Cryptography;
using System.Text;
using Functions.DomainService.Models;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// Content hashes for a function's source.
    /// <para>
    /// These decide two things: whether the editor has changes that are not deployed, and
    /// whether a build can be reused. Both mean the hash has to be stable across trivial
    /// differences that do not change behaviour — line endings and trailing whitespace,
    /// typically introduced by an editor rather than by the tenant — and sensitive to
    /// everything that does.
    /// </para>
    /// <para>
    /// The manifest hash is separate from the code hash because a dependency change requires a
    /// new image while a source-only change can reuse the dependency layer (DECISIONS D3).
    /// </para>
    /// </summary>
    public static class FunctionHashing
    {
        /// <summary>sha256 over the whole source: index, manifest and lockfile.</summary>
        public static string SourceHash(FunctionSource source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return Sha256(
                Normalise(source.IndexJs),
                Normalise(source.PackageJson),
                Normalise(source.LockJson ?? string.Empty));
        }

        /// <summary>sha256 over the entry point alone.</summary>
        public static string CodeHash(FunctionSource source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return Sha256(Normalise(source.IndexJs));
        }

        /// <summary>sha256 over the manifest and lockfile — the dependency identity.</summary>
        public static string ManifestHash(FunctionSource source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return Sha256(Normalise(source.PackageJson), Normalise(source.LockJson ?? string.Empty));
        }

        /// <summary>
        /// True when the editor's source differs from what the given version was built from.
        /// A function with no active version is dirty by definition: nothing is deployed.
        /// </summary>
        public static bool IsDirty(FunctionSource editorSource, string? activeVersionSourceHash)
        {
            if (string.IsNullOrEmpty(activeVersionSourceHash)) return true;
            return !string.Equals(SourceHash(editorSource), activeVersionSourceHash, StringComparison.Ordinal);
        }

        /// <summary>
        /// Removes differences an editor introduces but a runtime never sees: Windows versus
        /// Unix line endings, a trailing newline, and trailing whitespace on each line.
        /// Anything else — including indentation and blank lines inside the file — is content
        /// and is preserved.
        /// </summary>
        private static string Normalise(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            // CRLF first, then any lone CR, so a mixed-ending file collapses to one form
            // without inventing blank lines.
            var unified = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                              .Replace("\r", "\n", StringComparison.Ordinal);

            var lines = unified.Split('\n');
            for (var i = 0; i < lines.Length; i++) lines[i] = lines[i].TrimEnd();

            return string.Join('\n', lines).TrimEnd('\n');
        }

        /// <summary>
        /// Hashes parts with an explicit length prefix each, so concatenation cannot be
        /// ambiguous: ("ab", "c") and ("a", "bc") must not produce the same digest.
        /// </summary>
        private static string Sha256(params string[] parts)
        {
            var buffer = new StringBuilder();
            foreach (var part in parts)
            {
                buffer.Append(part.Length).Append(':').Append(part).Append(' ');
            }

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(buffer.ToString()));
            return Convert.ToHexStringLower(bytes);
        }
    }
}

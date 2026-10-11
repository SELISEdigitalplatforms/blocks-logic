using System.Text.Json;

namespace Blocks.FunctionRunner.Builds
{
    /// <summary>
    /// Screens a tenant's <c>package-lock.json</c> before <c>npm ci</c> installs from it (F-8).
    /// <para>
    /// Lockfiles used to be dropped because a lockfile pins the install to entries the manifest
    /// screening never saw: a transitive <c>resolved</c> URL can name any tarball, a git repository
    /// or a local path, which <see cref="SourceValidator"/>'s "registry ranges only" rule exists to
    /// prevent. Honouring one is safe only with the same rule applied to every entry, so: every
    /// installed package comes from the npm registry over HTTPS with an integrity hash, and nothing
    /// is a link or a path. What passes is pinned and reproducible; what does not fails the build
    /// with the entry named, rather than being installed some other way.
    /// </para>
    /// </summary>
    public static class LockfileValidator
    {
        /// <summary>The one lockfile honoured, and only at the bundle root.</summary>
        public const string FileName = "package-lock.json";

        /// <summary>
        /// A lockfile's own ceiling, outside the source's 2 MB: a few hundred packages already take
        /// a megabyte, and the file is data the install reads, not code that ships.
        /// </summary>
        public const long MaxBytes = 8 * 1024 * 1024;

        private const string Registry = "https://registry.npmjs.org/";

        public static ValidationResult Validate(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return ValidationResult.Fail("package-lock.json is empty");
            if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes)
                return ValidationResult.Fail($"package-lock.json is over the {MaxBytes} byte limit");

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                return ValidationResult.Fail($"package-lock.json is not valid JSON: {ex.Message}");
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return ValidationResult.Fail("package-lock.json must be a JSON object");

                // Version 1 has no "packages" map; npm ci would rewrite it from the network, which is
                // the unpinned install this exists to avoid.
                if (!root.TryGetProperty("lockfileVersion", out var version)
                    || version.ValueKind != JsonValueKind.Number
                    || version.GetInt32() is not (2 or 3))
                {
                    return ValidationResult.Fail(
                        "package-lock.json must be lockfileVersion 2 or 3 (npm 7 or later); regenerate it with a current npm");
                }

                if (!root.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Object)
                    return ValidationResult.Fail("package-lock.json has no \"packages\" map");

                foreach (var entry in packages.EnumerateObject())
                {
                    if (entry.Name.Length == 0) continue; // the function itself

                    var problem = ValidateEntry(entry.Name, entry.Value);
                    if (problem is not null) return ValidationResult.Fail($"package-lock.json: '{entry.Name}' {problem}");
                }
            }

            return ValidationResult.Pass();
        }

        private static string? ValidateEntry(string path, JsonElement entry)
        {
            if (!path.StartsWith("node_modules/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
                return "is not an installed package path (workspaces and local folders are not supported)";
            if (entry.ValueKind != JsonValueKind.Object) return "is not an object";

            if (entry.TryGetProperty("link", out var link) && link.ValueKind == JsonValueKind.True)
                return "is a link to a local folder, which is not supported";

            // Bundled inside its parent's tarball: it has no source of its own, and the parent's
            // integrity hash already covers it.
            if (entry.TryGetProperty("inBundle", out var inBundle) && inBundle.ValueKind == JsonValueKind.True)
                return null;

            if (!entry.TryGetProperty("resolved", out var resolved) || resolved.ValueKind != JsonValueKind.String)
                return "has no \"resolved\" URL";
            if (!resolved.GetString()!.StartsWith(Registry, StringComparison.Ordinal))
                return $"resolves outside the npm registry ({Short(resolved.GetString()!)}); only {Registry} is allowed";

            if (!entry.TryGetProperty("integrity", out var integrity) || integrity.ValueKind != JsonValueKind.String
                || !(integrity.GetString()!.StartsWith("sha512-", StringComparison.Ordinal)
                     || integrity.GetString()!.StartsWith("sha1-", StringComparison.Ordinal)))
                return "has no integrity hash";

            return null;
        }

        private static string Short(string value) => value.Length <= 80 ? value : value[..80] + "…";
    }
}

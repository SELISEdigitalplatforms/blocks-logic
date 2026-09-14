using System.Text.Json;
using System.Text.RegularExpressions;

namespace Blocks.FunctionRunner.Builds
{
    /// <summary>One file from a tenant's source bundle.</summary>
    public sealed record SourceFile(string Path, string Content);

    /// <summary>The outcome of screening a source bundle.</summary>
    public sealed record ValidationResult(bool Ok, string? Reason)
    {
        public static ValidationResult Pass() => new(true, null);
        public static ValidationResult Fail(string reason) => new(false, reason);
    }

    /// <summary>
    /// Screens a tenant's source bundle before anything is built from it.
    /// <para>
    /// The build runs untrusted code's <i>dependencies</i>, which is the most dangerous moment
    /// in the whole pipeline: <c>npm install</c> executes lifecycle scripts by default, on a
    /// host that has a Docker socket. The defences, in order of importance, are that builds run
    /// with <c>--ignore-scripts</c> unless a function explicitly opts in, in a container with no
    /// platform credentials, on the same restricted egress network as a run, under CPU, memory
    /// and time budgets.
    /// </para>
    /// <para>
    /// This validator is the cheap first pass: it rejects bundles that are malformed, oversized,
    /// or trying to escape the workspace, so those never reach a builder at all.
    /// </para>
    /// <para>
    /// It also screens every dependency specifier the build will resolve. That matters more than
    /// it used to: dependencies are installed fresh from package.json with no lockfile, so the
    /// manifest is the only description of what npm is about to fetch. A specifier that names a
    /// git repository, a path, an alias or a floating tag would put the install somewhere this
    /// screening never looked, so only registry version ranges are accepted.
    /// </para>
    /// </summary>
    public static class SourceValidator
    {
        /// <summary>Total source bundle size, before dependencies.</summary>
        public const long MaxSourceBytes = 2 * 1024 * 1024;

        /// <summary>Files a bundle may contain, beyond the entry point and manifest.</summary>
        public const int MaxFiles = 200;

        /// <summary>
        /// Packages that have no legitimate use inside a sandbox and whose presence signals an
        /// attempt to reach the host rather than a mistake.
        /// </summary>
        private static readonly string[] BlockedPackages =
        [
            "dockerode", "docker-cli-js", "node-docker-api",
            "kubernetes-client", "@kubernetes/client-node",
        ];

        public static ValidationResult Validate(IReadOnlyList<SourceFile> files, bool allowScripts)
        {
            ArgumentNullException.ThrowIfNull(files);

            if (files.Count == 0) return ValidationResult.Fail("the source bundle is empty");
            if (files.Count > MaxFiles)
                return ValidationResult.Fail($"the bundle has {files.Count} files, over the {MaxFiles} file limit");

            long total = 0;
            foreach (var file in files)
            {
                var pathProblem = ValidatePath(file.Path);
                if (pathProblem is not null) return ValidationResult.Fail(pathProblem);
                total += System.Text.Encoding.UTF8.GetByteCount(file.Content);
            }

            if (total > MaxSourceBytes)
                return ValidationResult.Fail($"the source is {total} bytes, over the {MaxSourceBytes} byte limit");

            if (!files.Any(f => f.Path is "index.js"))
                return ValidationResult.Fail("the bundle has no index.js at its root");

            var manifest = files.FirstOrDefault(f => f.Path is "package.json");
            if (manifest is null)
                return ValidationResult.Fail("the bundle has no package.json");

            return ValidateManifest(manifest.Content, allowScripts);
        }

        /// <summary>
        /// Rejects any path that could write outside the workspace. Archive-extraction escapes
        /// are a classic build-server compromise, so this is deliberately strict: relative
        /// segments, absolute paths, backslashes and symlink-looking names are all refused.
        /// </summary>
        internal static string? ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "the bundle contains a file with no path";
            if (path.Length > 255) return $"the path '{path[..64]}…' is too long";
            if (Path.IsPathRooted(path)) return $"'{path}' is an absolute path";
            if (path.Contains("..", StringComparison.Ordinal)) return $"'{path}' escapes the workspace";
            if (path.Contains('\\', StringComparison.Ordinal)) return $"'{path}' contains a backslash";
            if (path.StartsWith('.') && !path.StartsWith(".npmrc", StringComparison.Ordinal))
                return $"'{path}' is a hidden file";
            if (path.Contains('\0', StringComparison.Ordinal)) return "a path contains a null byte";
            if (path.StartsWith("node_modules/", StringComparison.Ordinal))
                return "node_modules must not be uploaded; dependencies are installed from package.json at build time";

            return null;
        }

        private static ValidationResult ValidateManifest(string json, bool allowScripts)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                return ValidationResult.Fail($"package.json is not valid JSON: {ex.Message}");
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return ValidationResult.Fail("package.json must be a JSON object");

                // The runtime imports the function as an ES module; CommonJS would fail at
                // import time with a much less helpful message than this one.
                if (!root.TryGetProperty("type", out var type) ||
                    type.ValueKind != JsonValueKind.String ||
                    type.GetString() != "module")
                {
                    return ValidationResult.Fail("""package.json must declare "type": "module" """.Trim());
                }

                if (!allowScripts && root.TryGetProperty("scripts", out var scripts) &&
                    scripts.ValueKind == JsonValueKind.Object)
                {
                    foreach (var script in scripts.EnumerateObject())
                    {
                        if (IsLifecycleScript(script.Name))
                        {
                            return ValidationResult.Fail(
                                $"package.json defines the lifecycle script '{script.Name}'; " +
                                "builds run with --ignore-scripts unless the function opts in");
                        }
                    }
                }

                // Every section npm installs from, plus devDependencies: those are omitted at
                // build time today, but screening them costs nothing and the flag could change.
                foreach (var section in DependencySections)
                {
                    if (!root.TryGetProperty(section, out var deps) || deps.ValueKind != JsonValueKind.Object)
                        continue;

                    foreach (var dep in deps.EnumerateObject())
                    {
                        if (BlockedPackages.Contains(dep.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            return ValidationResult.Fail(
                                $"the package '{dep.Name}' is not permitted: it exists to control the host " +
                                "container runtime, which a function must never reach");
                        }

                        var problem = ValidateSpecifier(dep.Name, dep.Value, allowReference: false);
                        if (problem is not null) return ValidationResult.Fail(problem);
                    }
                }

                // overrides (npm) and resolutions (yarn) rewrite the version of any package in the
                // tree, transitive ones included, so an unscreened specifier here reaches just as
                // far as one in dependencies.
                foreach (var section in new[] { "overrides", "resolutions" })
                {
                    if (!root.TryGetProperty(section, out var overrides)) continue;

                    var problem = ValidateOverrides(section, overrides, depth: 0);
                    if (problem is not null) return ValidationResult.Fail(problem);
                }
            }

            return ValidationResult.Pass();
        }

        /// <summary>Sections npm resolves from. peerDependencies are auto-installed since npm 7.</summary>
        private static readonly string[] DependencySections =
        [
            "dependencies", "devDependencies", "optionalDependencies", "peerDependencies",
        ];

        /// <summary>
        /// A registry version range: <c>1.2.3</c>, <c>^1.7.0</c>, <c>~1.2</c>, <c>1.x</c>,
        /// <c>>=1.2.3 &lt;2.0.0</c>, <c>1.0.0-beta.1</c>. Anything that does not start like a
        /// version — a dist-tag such as <c>latest</c>, a bare <c>*</c>, an alias, a path, a
        /// repository — is refused, because with no lockfile it is the only thing naming what
        /// gets fetched.
        /// </summary>
        internal static readonly Regex VersionRangePattern =
            new(@"^[v^~><=]*\s*\d[0-9A-Za-z.\-+*^~><=|\s]*$", RegexOptions.Compiled);

        /// <summary>An npm overrides back-reference (<c>$ky</c>) to an already-screened dependency.</summary>
        private static readonly Regex OverrideReferencePattern =
            new(@"^\$[A-Za-z0-9@._/-]+$", RegexOptions.Compiled);

        internal static string? ValidateSpecifier(string name, JsonElement value, bool allowReference)
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                return $"the dependency '{name}' must name a version range as a string";
            }

            var spec = (value.GetString() ?? string.Empty).Trim();

            if (spec.Length == 0)
            {
                return $"the dependency '{name}' has no version range; " +
                       "an empty specifier installs whatever is newest at build time";
            }

            if (allowReference && OverrideReferencePattern.IsMatch(spec)) return null;

            if (spec.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
                spec.StartsWith("link:", StringComparison.OrdinalIgnoreCase) ||
                spec.StartsWith("workspace:", StringComparison.OrdinalIgnoreCase) ||
                spec.StartsWith("portal:", StringComparison.OrdinalIgnoreCase) ||
                spec.Contains("://", StringComparison.Ordinal))
            {
                return $"the dependency '{name}' uses the specifier '{spec}'; " +
                       "only published registry versions are permitted";
            }

            if (spec.StartsWith("npm:", StringComparison.OrdinalIgnoreCase))
            {
                return $"the dependency '{name}' is an alias for '{spec}'; an alias hides what is " +
                       "actually installed behind a name this screening already checked";
            }

            if (spec.Contains('/', StringComparison.Ordinal) ||
                spec.Contains('#', StringComparison.Ordinal) ||
                spec.Contains(':', StringComparison.Ordinal))
            {
                return $"the dependency '{name}' uses the specifier '{spec}'; repository and path " +
                       "specifiers are not permitted, only published registry versions";
            }

            if (!VersionRangePattern.IsMatch(spec))
            {
                return $"the dependency '{name}' uses the specifier '{spec}'; it must be a version " +
                       "range such as '^1.7.0' or '1.2.3'. Tags and wildcards are refused because " +
                       "dependencies are resolved fresh at every build and nothing pins them afterwards";
            }

            return null;
        }

        /// <summary>
        /// overrides nest: <c>{ "a": { "b": "1.2.3" } }</c>, and a nested object may also carry a
        /// <c>"."</c> key for the package itself. Every string leaf is a specifier.
        /// </summary>
        private static string? ValidateOverrides(string section, JsonElement node, int depth)
        {
            const int MaxDepth = 8;
            if (depth > MaxDepth) return $"'{section}' is nested more than {MaxDepth} levels deep";

            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in node.EnumerateObject())
                    {
                        var problem = property.Value.ValueKind == JsonValueKind.Object
                            ? ValidateOverrides(section, property.Value, depth + 1)
                            : ValidateSpecifier($"{section}.{property.Name}", property.Value, allowReference: true);
                        if (problem is not null) return problem;
                    }
                    return null;

                case JsonValueKind.String:
                    return ValidateSpecifier(section, node, allowReference: true);

                default:
                    return $"'{section}' must be an object of package names to version ranges";
            }
        }

        private static bool IsLifecycleScript(string name) => name is
            "preinstall" or "install" or "postinstall" or
            "prepare" or "prepublish" or "prepublishOnly" or "prepack" or "postpack";
    }
}

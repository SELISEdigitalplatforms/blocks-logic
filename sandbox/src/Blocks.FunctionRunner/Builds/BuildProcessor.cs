using System.Globalization;
using System.Text;
using System.Text.Json;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Docker.DotNet;
using Docker.DotNet.Models;
using ICSharpCode.SharpZipLib.Tar;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Builds
{
    /// <summary>
    /// Turns a tenant's source bundle into an immutable, digest-pinned image.
    /// <para>
    /// This is the most dangerous step in the pipeline, because installing dependencies means
    /// running code the tenant chose on a host that can reach the Docker Engine. It is confined
    /// the same way an execution is: <c>--ignore-scripts</c> unless the function explicitly opts
    /// in, no platform credentials in the build environment, the same restricted egress network
    /// as a run, and hard CPU, memory and time budgets. What is built is content-addressed, so
    /// Test and Deploy share one image (DECISIONS D3).
    /// </para>
    /// <para>
    /// Dependencies are always resolved fresh from <c>package.json</c>: lockfiles are not accepted
    /// from tenants and never reach the build context. That trades reproducibility for freshness,
    /// so the compensating control is that the versions npm actually resolved are read back out of
    /// the build log and stored on the build record, rather than assumed from the manifest's
    /// ranges — a range is a request, and only the resolved list says what shipped.
    /// </para>
    /// </summary>
    public sealed class BuildProcessor
    {
        /// <summary>
        /// Files that would pin an install to versions the platform never screened. They are
        /// dropped from the bundle here and removed again inside the image, because a lockfile
        /// is exactly the thing this pipeline no longer honours.
        /// </summary>
        internal static readonly string[] LockfileNames =
        [
            "package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml",
        ];

        private readonly IDatabase _db;
        private readonly IDockerClient _docker;
        private readonly RunnerOptions _options;
        private readonly ILogger<BuildProcessor> _logger;
        private readonly string _template;

        public BuildProcessor(
            IDatabase db,
            IDockerClient docker,
            IOptions<RunnerOptions> options,
            ILogger<BuildProcessor> logger)
        {
            _db = db;
            _docker = docker;
            _options = options.Value;
            _logger = logger;
            _template = LoadTemplate();
        }

        public async Task ProcessAsync(BuildJob job, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(job);

            var workspace = Path.Combine(_options.BuildsDir, job.BuildId);
            var log = new StringBuilder();

            // Unguessable per build, so a lifecycle script — the one thing in a build that can
            // write to this log at the tenant's direction — cannot echo a fake resolved-package
            // block and have it believed.
            var nonce = Guid.NewGuid().ToString("n");
            var beginMarker = PackagesMarker(nonce, "begin");
            var endMarker = PackagesMarker(nonce, "end");

            // The fenced block is machinery, not something a tenant should have to read, and at
            // a few hundred packages it would crowd the real npm output out of the 16 KB tail.
            string PublishableLog() => Tail(StripPackagesBlock(log.ToString(), beginMarker, endMarker));

            try
            {
                // --- fetch and screen the source ---------------------------------------
                var bundle = await _db.StringGetAsync(job.SourceKey).ConfigureAwait(false);
                if (!bundle.HasValue)
                {
                    await PublishAsync(job, "FAILED", null, null, log.ToString(),
                        $"the source bundle at '{job.SourceKey}' has expired or was never written").ConfigureAwait(false);
                    return;
                }

                List<SourceFile> files;
                try
                {
                    files = ParseBundle(bundle!);
                }
                catch (JsonException ex)
                {
                    await PublishAsync(job, "FAILED", null, null, log.ToString(),
                        $"the source bundle is not valid JSON: {ex.Message}").ConfigureAwait(false);
                    return;
                }

                var validation = SourceValidator.Validate(files, job.AllowScripts);
                if (!validation.Ok)
                {
                    _logger.LogWarning("Build {BuildId} rejected: {Reason}", job.BuildId, validation.Reason);
                    await PublishAsync(job, "FAILED", null, null, log.ToString(), validation.Reason)
                        .ConfigureAwait(false);
                    return;
                }

                // --- lay out the build context -----------------------------------------
                PrepareWorkspace(workspace, files);

                var npmFlags = job.AllowScripts ? "--omit=dev" : "--omit=dev --ignore-scripts";
                if (job.AllowScripts)
                {
                    _logger.LogWarning(
                        "Build {BuildId} runs npm lifecycle scripts at the function's request", job.BuildId);
                }

                var dockerfile = _template
                    .Replace("{{BASE_IMAGE}}", _options.BaseImage, StringComparison.Ordinal)
                    .Replace("{{NPM_FLAGS}}", npmFlags, StringComparison.Ordinal)
                    .Replace("{{MAX_OLD_SPACE_MB}}",
                        RunLimits.Default.MaxOldSpaceMb.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    .Replace("{{PACKAGES_BEGIN}}", beginMarker, StringComparison.Ordinal)
                    .Replace("{{PACKAGES_END}}", endMarker, StringComparison.Ordinal);
                await File.WriteAllTextAsync(Path.Combine(workspace, "Dockerfile"), dockerfile, token)
                    .ConfigureAwait(false);

                // --- build ---------------------------------------------------------------
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(_options.BuildTimeoutSeconds));

                var tag = job.ImageRef;
                var built = await BuildImageAsync(workspace, tag, log, timeout.Token).ConfigureAwait(false);
                if (!built)
                {
                    await PublishAsync(job, "FAILED", null, null, PublishableLog(), "the image build failed")
                        .ConfigureAwait(false);
                    return;
                }

                // --- publish and pin by digest -------------------------------------------
                var digest = await PushAsync(tag, log, token).ConfigureAwait(false);
                if (digest is null)
                {
                    await PublishAsync(job, "FAILED", null, null, PublishableLog(), "the image could not be pushed")
                        .ConfigureAwait(false);
                    return;
                }

                var installed = ExtractResolvedPackages(log.ToString(), beginMarker, endMarker);
                if (installed is null)
                {
                    _logger.LogWarning(
                        "Build {BuildId} produced no resolved-package list; falling back to the " +
                        "ranges package.json asked for, which are not what was installed", job.BuildId);
                }

                var packages = installed ?? DescribeRequestedPackages(files);

                // Deliberately not pinned in functions:images:keep here. A build is not a promise
                // that anything will use its image: pinning at build time meant every Test of
                // changed source pinned an image for good, and when the build record later expired
                // there was nothing left to say which digest that was. The control plane pins a
                // digest when it becomes a deployed version and unpins it when that version goes.
                // Until then the image is protected by Image GC's grace window, and a Test whose
                // image has been reclaimed rebuilds rather than failing (DECISIONS D3).
                await PublishAsync(job, "SUCCEEDED", digest, packages, PublishableLog(), null).ConfigureAwait(false);

                _logger.LogInformation("Build {BuildId} produced {Digest}", job.BuildId, digest);
            }
            catch (OperationCanceledException)
            {
                await PublishAsync(job, "FAILED", null, null, PublishableLog(),
                    $"the build exceeded its {_options.BuildTimeoutSeconds}s time limit").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Build {BuildId} failed unexpectedly", job.BuildId);
                await PublishAsync(job, "FAILED", null, null, PublishableLog(), ex.Message).ConfigureAwait(false);
            }
            finally
            {
                CleanUp(workspace);
            }
        }

        /// <summary>
        /// Lays out the context the template expects: <c>manifest/</c> holds package.json so the
        /// dependency layer caches independently, <c>src/</c> holds everything else.
        /// <para>
        /// Lockfiles are dropped rather than copied. A build resolves dependencies fresh from
        /// package.json, and a lockfile in the context would quietly override that with pinned
        /// versions — including transitive ones the manifest screening never saw.
        /// </para>
        /// </summary>
        internal static void PrepareWorkspace(string workspace, IReadOnlyList<SourceFile> files)
        {
            if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
            Directory.CreateDirectory(Path.Combine(workspace, "manifest"));
            Directory.CreateDirectory(Path.Combine(workspace, "src"));

            foreach (var file in files)
            {
                if (IsLockfile(file.Path)) continue;

                var isManifest = file.Path is "package.json";
                var root = Path.GetFullPath(Path.Combine(workspace, isManifest ? "manifest" : "src"));
                var full = Path.GetFullPath(Path.Combine(root, file.Path));

                // Belt and braces: the validator already refused escaping paths, but the file
                // system is the thing that would actually be damaged, so check again here. The
                // bound is the subdirectory, not the workspace: a single '..' stays inside the
                // workspace and would drop a tenant file beside the generated Dockerfile.
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"refusing to write '{file.Path}' outside the build context");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, file.Content);
            }
        }

        /// <summary>A lockfile anywhere in the bundle, not only at its root.</summary>
        internal static bool IsLockfile(string path) =>
            LockfileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

        private async Task<bool> BuildImageAsync(string workspace, string tag, StringBuilder log, CancellationToken token)
        {
            using var context = CreateTarContext(workspace);

            var parameters = new ImageBuildParameters
            {
                Dockerfile = "Dockerfile",
                Tags = [tag],
                Remove = true,
                ForceRemove = true,
                NoCache = false,
                // The build gets the same confined egress as an execution: npm reaches the public
                // registry and nothing private.
                NetworkMode = _options.Network,
                Memory = (long)_options.BuildMemoryMb * 1024 * 1024,
                MemorySwap = (long)_options.BuildMemoryMb * 1024 * 1024,
                CPUQuota = _options.BuildCpus * 100_000L,
                CPUPeriod = 100_000L,
                Labels = new Dictionary<string, string> { ["dev.selise.blocks.function"] = "true" },
            };

            var failed = false;
            var progress = new Progress<JSONMessage>(message =>
            {
                if (!string.IsNullOrEmpty(message.Stream)) log.Append(message.Stream);
                if (!string.IsNullOrEmpty(message.ErrorMessage))
                {
                    log.Append(message.ErrorMessage);
                    failed = true;
                }
            });

            try
            {
                await _docker.Images.BuildImageFromDockerfileAsync(
                    parameters, context, authConfigs: null, headers: null, progress, token).ConfigureAwait(false);
            }
            catch (DockerApiException ex)
            {
                log.Append(ex.Message);
                return false;
            }

            return !failed;
        }

        private async Task<string?> PushAsync(string tag, StringBuilder log, CancellationToken token)
        {
            var (name, imageTag) = Sandbox.ImageResolver.SplitReference(tag);
            try
            {
                await _docker.Images.PushImageAsync(
                    name,
                    new ImagePushParameters { Tag = imageTag },
                    authConfig: null,
                    new Progress<JSONMessage>(m =>
                    {
                        if (!string.IsNullOrEmpty(m.ErrorMessage)) log.Append(m.ErrorMessage);
                    }),
                    token).ConfigureAwait(false);

                var inspect = await _docker.Images.InspectImageAsync(tag, token).ConfigureAwait(false);
                return inspect.RepoDigests?.FirstOrDefault() ?? inspect.ID;
            }
            catch (DockerApiException ex)
            {
                log.Append(ex.Message);
                return null;
            }
        }

        /// <summary>Packs the workspace into the tar stream the Engine's build API expects.</summary>
        private static MemoryStream CreateTarContext(string workspace)
        {
            var buffer = new MemoryStream();
            using (var archive = TarArchive.CreateOutputTarArchive(buffer, TarBuffer.DefaultBlockFactor))
            {
                archive.IsStreamOwner = false;
                archive.RootPath = workspace.Replace('\\', '/').TrimEnd('/');

                foreach (var path in Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories))
                {
                    var entry = TarEntry.CreateEntryFromFile(path);
                    entry.Name = Path.GetRelativePath(workspace, path).Replace('\\', '/');
                    archive.WriteEntry(entry, recurse: false);
                }
            }

            buffer.Position = 0;
            return buffer;
        }

        private static List<SourceFile> ParseBundle(string json)
        {
            // The control plane sends { "files": { "path": "content", … } }.
            using var doc = JsonDocument.Parse(json);
            var files = new List<SourceFile>();

            if (!doc.RootElement.TryGetProperty("files", out var map) || map.ValueKind != JsonValueKind.Object)
            {
                return files;
            }

            foreach (var property in map.EnumerateObject())
            {
                files.Add(new SourceFile(property.Name, property.Value.GetString() ?? string.Empty));
            }
            return files;
        }

        /// <summary>At most this many packages are recorded on a build, so one pathological
        /// manifest cannot write an unbounded document into the build record.</summary>
        internal const int MaxPackagesRecorded = 500;

        internal static string PackagesMarker(string nonce, string kind) =>
            $"---blocks-packages-{nonce}-{kind}---";

        /// <summary>
        /// The versions npm actually resolved, read back from the <c>npm ls</c> block the build
        /// fenced with this build's nonce. Returns <c>null</c> — not an empty list — when the
        /// block is absent or unreadable, so the caller can say so rather than claim a
        /// dependency-free function.
        /// <para>
        /// Markers are matched as whole lines: the Engine echoes the whole RUN instruction into
        /// the log, so both markers appear there too, inside a longer line. The last fenced block
        /// wins, which is the one this build wrote.
        /// </para>
        /// </summary>
        internal static string? ExtractResolvedPackages(string log, string beginMarker, string endMarker)
        {
            if (string.IsNullOrEmpty(log)) return null;

            var lines = log.Split('\n');
            var begin = -1;
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                if (lines[i].Trim() == beginMarker) { begin = i; break; }
            }
            if (begin < 0) return null;

            var end = -1;
            for (var i = begin + 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() == endMarker) { end = i; break; }
            }
            // An unterminated block means the build died mid-print; nothing in it can be trusted.
            if (end < 0) return null;

            var json = string.Join('\n', lines[(begin + 1)..end]).Trim();
            if (json.Length == 0) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

                var packages = new List<object>();
                if (doc.RootElement.TryGetProperty("dependencies", out var deps) &&
                    deps.ValueKind == JsonValueKind.Object)
                {
                    foreach (var dep in deps.EnumerateObject())
                    {
                        if (packages.Count >= MaxPackagesRecorded) break;
                        var version = dep.Value.ValueKind == JsonValueKind.Object &&
                                      dep.Value.TryGetProperty("version", out var v) &&
                                      v.ValueKind == JsonValueKind.String
                            ? v.GetString()
                            : null;
                        packages.Add(new { name = dep.Name, version });
                    }
                }

                return JsonSerializer.Serialize(packages);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Removes the fenced block, markers included, from a log shown to tenants.</summary>
        internal static string StripPackagesBlock(string log, string beginMarker, string endMarker)
        {
            if (string.IsNullOrEmpty(log)) return log;
            if (!log.Contains(beginMarker, StringComparison.Ordinal)) return log;

            var kept = new List<string>();
            var inside = false;
            foreach (var line in log.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!inside && trimmed == beginMarker) { inside = true; continue; }
                if (inside)
                {
                    if (trimmed == endMarker) inside = false;
                    continue;
                }
                kept.Add(line);
            }

            // The Engine echoes the whole RUN instruction as a Step line, so the markers also
            // appear inside a line that is worth keeping. Blank them there rather than dropping
            // the step: what is left reads as a normal command.
            return string.Join('\n', kept)
                .Replace(beginMarker, string.Empty, StringComparison.Ordinal)
                .Replace(endMarker, string.Empty, StringComparison.Ordinal);
        }

        /// <summary>
        /// What the manifest asked for — used only when the resolved list could not be read.
        /// These are ranges, not versions: <c>^1.7.0</c> is a request, and with no lockfile in
        /// the pipeline two builds of it can legitimately differ.
        /// </summary>
        internal static string DescribeRequestedPackages(IReadOnlyList<SourceFile> files)
        {
            var manifest = files.FirstOrDefault(f => f.Path is "package.json");
            if (manifest is null) return "[]";

            try
            {
                using var doc = JsonDocument.Parse(manifest.Content);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return "[]";

                var packages = new List<object>();
                // devDependencies are omitted because --omit=dev means they are never installed.
                foreach (var section in new[] { "dependencies", "optionalDependencies" })
                {
                    if (!doc.RootElement.TryGetProperty(section, out var deps) ||
                        deps.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var dep in deps.EnumerateObject())
                    {
                        if (packages.Count >= MaxPackagesRecorded) break;
                        var requested = dep.Value.ValueKind == JsonValueKind.String ? dep.Value.GetString() : null;
                        packages.Add(new { name = dep.Name, version = requested });
                    }
                }

                return JsonSerializer.Serialize(packages);
            }
            catch (JsonException)
            {
                return "[]";
            }
        }

        private Task<RedisValue> PublishAsync(BuildJob job, string status, string? digest, string? packages, string? log, string? error)
            => _db.StreamAddAsync(RedisKeys.BuildResultsStream,
            [
                new NameValueEntry("buildId", job.BuildId),
                new NameValueEntry("functionId", job.FunctionId),
                new NameValueEntry("tenantId", job.TenantId ?? string.Empty),
                new NameValueEntry("status", status),
                new NameValueEntry("imageDigest", digest ?? string.Empty),
                new NameValueEntry("packages", packages ?? "[]"),
                new NameValueEntry("log", log ?? string.Empty),
                new NameValueEntry("errorMessage", error ?? string.Empty),
                new NameValueEntry("protocol", RedisKeys.ProtocolVersion),
            ]);

        /// <summary>Build logs are shown to tenants; keep the tail, which is where failures are.</summary>
        private static string Tail(StringBuilder log, int max = 16 * 1024) => Tail(log.ToString(), max);

        private static string Tail(string text, int max = 16 * 1024) =>
            text.Length <= max ? text : text[^max..];

        private static string LoadTemplate()
        {
            // The template ships beside the binaries so a change to it is a deployment, not a
            // rebuild of the runner.
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "function.Dockerfile.tmpl"),
                "/opt/blocks-function-runner/function.Dockerfile.tmpl",
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
            }

            throw new FileNotFoundException(
                "function.Dockerfile.tmpl was not found; it must be deployed beside the runner",
                candidates[0]);
        }

        private void CleanUp(string workspace)
        {
            try
            {
                if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
            }
            catch (IOException ex)
            {
                _logger.LogWarning("Could not remove build workspace {Dir}: {Message}", workspace, ex.Message);
            }
        }
    }
}

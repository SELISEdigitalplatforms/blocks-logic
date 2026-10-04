using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Blocks.FunctionRunner.Contracts;

namespace Blocks.FunctionRunner.Runs
{
    /// <summary>
    /// Writes the per-run execution envelope to disk for the sandbox to read.
    /// <para>
    /// The envelope is the only channel into a sandbox, so this is where the platform's promise
    /// that "the function receives identity metadata, not privileged credentials" (spec §17) is
    /// kept. Envelopes are screened for forbidden keys before they are written: if the control
    /// plane ever sends a token by mistake, the run fails here rather than leaking it.
    /// </para>
    /// </summary>
    public static class ExecutionEnvelope
    {
        /// <summary>
        /// Substrings that must never appear as a key in an envelope outside <c>env</c> and
        /// <c>input</c>. Matched case-insensitively against every property name at every depth.
        /// </summary>
        private static readonly string[] ForbiddenKeyFragments =
        [
            "accesstoken", "access_token", "refreshtoken", "refresh_token",
            "clientsecret", "client_secret", "servicekey", "service_account",
            "connectionstring", "connection_string", "password", "passwd",
            "vaulttoken", "vault_token", "apikey", "api_key", "privatekey", "private_key",
            "secret", "credential", "bearer", "authorization",
        ];

        /// <summary>Thrown when an envelope fails screening. The run must not start.</summary>
        public sealed class ForbiddenContentException(string message) : Exception(message);

        /// <summary>
        /// Thrown when the envelope could not be made readable to the sandbox without also being
        /// readable to everyone else on the host. A host fault, not the tenant's: the run must
        /// not start, and the envelope is already gone by the time this is seen.
        /// </summary>
        public sealed class HandoffException(string message) : Exception(message);

        /// <summary>The envelope's file name inside a run directory.</summary>
        public const string FileName = "execution.json";

        /// <summary>
        /// Writes <paramref name="envelopeJson"/> into <paramref name="runDir"/> and returns the
        /// file path.
        /// </summary>
        /// <remarks>
        /// The envelope carries the tenant's resolved secret-bound variables, so it is readable by
        /// the runner and the sandbox and by nobody else on the host: the directory is 0700 and
        /// the file 0440, owned by the runner with its group set to the sandbox's gid
        /// (<see cref="Ceilings.SandboxUid"/>). The sandbox runs as <c>10001:10001</c> and reads it
        /// through group permission. The runner is unprivileged and cannot chown a file to another
        /// user, but it may give its own file to a group it belongs to, which is why
        /// <c>provision/40-runner-user.sh</c> makes it a member of that gid. The directory needs no
        /// traversal bit for the sandbox: the Engine resolves the bind source as root, and the
        /// mount point is the file itself, so only the file's own permissions are checked.
        /// <para>
        /// The file is created 0600 and only widened to the group once the group is the
        /// sandbox's, so it is never world-readable, not even for an instant. If that handoff
        /// fails the file is deleted and <see cref="HandoffException"/> is thrown: the fallback
        /// the previous 0644 file represented is exactly what this exists to remove.
        /// </para>
        /// </remarks>
        /// <param name="runDir">The run's own directory.</param>
        /// <param name="envelopeJson">The envelope as the sandbox will read it.</param>
        /// <param name="delegatedToken">
        /// True when the runner itself added <c>blocks.accessToken</c> from the run's delegation
        /// grant (<see cref="RunDelegation"/>). Exempts that one path from the screen and nothing
        /// else; see <see cref="Screen"/>.
        /// </param>
        public static string Write(string runDir, string envelopeJson, bool delegatedToken = false)
            => Write(runDir, envelopeJson, GiveToSandboxGroup, delegatedToken);

        /// <summary>
        /// <see cref="Write(string, string)"/> with the group handoff supplied, so the failure
        /// path can be exercised without a host where chown fails.
        /// </summary>
        internal static string Write(
            string runDir, string envelopeJson, Action<string, int> setGroup, bool delegatedToken = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runDir);
            ArgumentException.ThrowIfNullOrWhiteSpace(envelopeJson);
            ArgumentNullException.ThrowIfNull(setGroup);

            var bytes = Encoding.UTF8.GetByteCount(envelopeJson);
            if (bytes > Ceilings.InputBytes)
            {
                throw new ForbiddenContentException(
                    $"the execution envelope is {bytes} bytes, over the {Ceilings.InputBytes} byte input ceiling");
            }

            Screen(envelopeJson, delegatedToken);

            // 0700 whether it is new or left over from an earlier attempt at the same run.
            Directory.CreateDirectory(runDir, OwnerOnlyDirectory);
            File.SetUnixFileMode(runDir, OwnerOnlyDirectory);

            var path = Path.Combine(runDir, FileName);

            // A file left by a crashed attempt keeps whatever mode it was created with (an older
            // runner wrote 0644); creating afresh is the only way the mode below is the real one.
            File.Delete(path);

            try
            {
                using (var stream = new FileStream(path, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                }))
                {
                    stream.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(envelopeJson));
                }

                setGroup(path, Ceilings.SandboxUid);

                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead);
            }
            catch
            {
                TryDelete(path);
                throw;
            }

            return path;
        }

        /// <summary>
        /// Proves this host can hand an envelope to the sandbox group, without a run at stake.
        /// The startup guard calls it so that a host where the runner is not a member of the
        /// sandbox gid reports itself unhealthy and claims nothing, rather than failing every run
        /// it picks up.
        /// </summary>
        /// <returns>null when the handoff works, otherwise why it does not.</returns>
        internal static string? ProbeHandoff(string runsDir, Action<string, int>? setGroup = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runsDir);

            var probe = Path.Combine(runsDir, $".probe-envelope-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(runsDir);
                File.WriteAllText(probe, "{}");
                (setGroup ?? GiveToSandboxGroup)(probe, Ceilings.SandboxUid);
                return null;
            }
            catch (Exception ex) when (ex is HandoffException or IOException or UnauthorizedAccessException)
            {
                return ex.Message;
            }
            finally
            {
                TryDelete(probe);
            }
        }

        private const UnixFileMode OwnerOnlyDirectory =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        private static void GiveToSandboxGroup(string path, int gid)
        {
            // uid (uid_t)-1 leaves the owner unchanged; only the group moves.
            if (NativeMethods.Chown(path, uint.MaxValue, (uint)gid) != 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                throw new HandoffException(
                    $"could not give the execution envelope to the sandbox group (gid {gid}): " +
                    $"{Marshal.GetLastPInvokeErrorMessage()} (errno {errno}). The runner's user must be a " +
                    $"member of gid {gid}; run provision/40-runner-user.sh and restart the runner");
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // The run directory is removed after the run regardless; this is the early copy.
            }
            catch (UnauthorizedAccessException)
            {
                // As above.
            }
        }

        private static class NativeMethods
        {
            [DllImport("libc", EntryPoint = "chown", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern int Chown([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint owner, uint group);
        }

        /// <summary>
        /// Rejects an envelope carrying anything that looks like a credential, and rejects
        /// malformed JSON outright.
        /// <para>
        /// Two top-level subtrees are exempt, and only those two:
        /// <list type="bullet">
        /// <item><c>env</c> — its keys are tenant-authored variable names, and a variable may
        /// deliberately carry a secret the tenant bound to it, so the honest name for one
        /// (<c>STRIPE_API_KEY</c>) must not fail the run;</item>
        /// <item><c>input</c> — the caller's own payload (for an HTTP trigger, its body and
        /// query), which the platform did not put there and cannot vouch for either way. A
        /// sign-up form posting <c>{"password": …}</c> is ordinary input, and refusing it
        /// protected nothing: the caller already holds what it sent.</item>
        /// </list>
        /// Everything the platform itself writes — <c>run</c>, <c>context</c>, <c>limits</c> and
        /// anything added later — is still screened at every depth, which is where a leaked
        /// platform credential would actually appear. Mirrors the control plane's
        /// <c>FunctionEnvelopeBuilder.Screen</c> — the two screens are meant to agree, so a
        /// change to one belongs in the other.
        /// </para>
        /// </summary>
        /// <param name="envelopeJson">The envelope to screen.</param>
        /// <param name="delegatedToken">
        /// Exempts exactly the top-level <c>blocks.accessToken</c> — the caller's delegated token,
        /// which only the runner adds, after the queued envelope has been screened without this
        /// flag. Anything else credential-shaped, including under <c>blocks</c>, is still refused,
        /// and a queued envelope carrying that key is refused before any grant is redeemed.
        /// </param>
        public static void Screen(string envelopeJson, bool delegatedToken = false)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(envelopeJson);
            }
            catch (JsonException ex)
            {
                throw new ForbiddenContentException($"the execution envelope is not valid JSON: {ex.Message}");
            }

            using (doc)
            {
                Walk(doc.RootElement, string.Empty, screenKeys: true, delegatedToken);
            }
        }

        private static void Walk(JsonElement element, string path, bool screenKeys, bool delegatedToken)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var name = property.Name;

                        // Only the envelope's own top-level `env` and `input`. An object
                        // merely called "env" or "input" somewhere under `context` is not the
                        // same thing and is still screened. `input.headers` is screened again:
                        // the control plane builds it from an allow-list, so a credential there
                        // is a control-plane mistake, not caller data — the same rule the
                        // control plane's own screen applies.
                        var childScreens = screenKeys
                            ? !(path.Length == 0 && (name == "env" || name == "input"))
                            : path == "input." && name == "headers";

                        var runnerAddedToken = delegatedToken && path == "blocks." && name == "accessToken";

                        if (screenKeys && !runnerAddedToken)
                        {
                            foreach (var fragment in ForbiddenKeyFragments)
                            {
                                if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                                {
                                    throw new ForbiddenContentException(
                                        $"the execution envelope contains a forbidden key at '{path}{name}'; " +
                                        "credentials must never reach a sandbox");
                                }
                            }
                        }
                        Walk(property.Value, $"{path}{name}.", childScreens, delegatedToken);
                    }
                    break;

                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item, $"{path}[{index++}].", screenKeys, delegatedToken);
                    }
                    break;

                default:
                    break;
            }
        }
    }
}

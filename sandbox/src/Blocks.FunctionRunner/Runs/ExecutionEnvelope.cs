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
        /// Substrings that must never appear as a key anywhere in an envelope. Matched
        /// case-insensitively against every property name at every depth.
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
        /// Writes <paramref name="envelopeJson"/> into <paramref name="runDir"/> and returns the
        /// file path.
        /// </summary>
        /// <remarks>
        /// The directory is 0751 and the file 0644: the sandbox runs as uid 10001, which is not
        /// the runner's uid, so it needs to traverse in and read the file while being unable to
        /// list its siblings.
        /// </remarks>
        public static string Write(string runDir, string envelopeJson)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runDir);
            ArgumentException.ThrowIfNullOrWhiteSpace(envelopeJson);

            var bytes = Encoding.UTF8.GetByteCount(envelopeJson);
            if (bytes > Ceilings.InputBytes)
            {
                throw new ForbiddenContentException(
                    $"the execution envelope is {bytes} bytes, over the {Ceilings.InputBytes} byte input ceiling");
            }

            Screen(envelopeJson);

            Directory.CreateDirectory(runDir);
            var path = Path.Combine(runDir, "execution.json");
            File.WriteAllText(path, envelopeJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            File.SetUnixFileMode(runDir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

            return path;
        }

        /// <summary>
        /// Rejects an envelope carrying anything that looks like a credential, and rejects
        /// malformed JSON outright.
        /// </summary>
        public static void Screen(string envelopeJson)
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
                Walk(doc.RootElement, string.Empty);
            }
        }

        private static void Walk(JsonElement element, string path)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var name = property.Name;
                        foreach (var fragment in ForbiddenKeyFragments)
                        {
                            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                            {
                                throw new ForbiddenContentException(
                                    $"the execution envelope contains a forbidden key at '{path}{name}'; " +
                                    "credentials must never reach a sandbox");
                            }
                        }
                        Walk(property.Value, $"{path}{name}.");
                    }
                    break;

                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item, $"{path}[{index++}].");
                    }
                    break;

                default:
                    break;
            }
        }
    }
}

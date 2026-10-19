using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Blocks.Genesis.Entities.Logic;
using Functions.DomainService.Dtos;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// The one place a function export file is written, read and hashed — shared by export, preview, apply and the
    /// tests, so the hash can never be computed two ways. Functions-only (2026-10-07: no shared code with Proxy).
    /// <para>
    /// <b>Canonical JSON</b>: the body serialized with <see cref="Options"/> (camelCase, enums by name, nulls written),
    /// then every object's keys sorted ordinally, no whitespace. Header dictionaries are objects too, so their order
    /// never changes the hash. <see cref="Hash"/> is the lower-case hex sha256 of its UTF-8 bytes.
    /// </para>
    /// </summary>
    public static class FunctionTransferCodec
    {
        public const string Format = "blocks.function.export";
        public const int SchemaVersion = 1;

        /// <summary>The whole request body an import may carry: 2 MB of index.js plus manifest, lockfile and settings.</summary>
        public const int MaxImportBytes = 3 * 1024 * 1024;

        /// <summary>The settings and files the preview diffs and an apply can choose between.</summary>
        public static class Fields
        {
            public const string Name = "name";
            public const string Description = "description";
            public const string Trigger = "trigger";
            public const string Limits = "limits";
            public const string Retry = "retry";
            public const string Variables = "variables";
            public const string OutputActions = "outputActions";
            public const string IndexJs = "indexJs";
            public const string PackageJson = "packageJson";
            public const string LockJson = "lockJson";

            public static readonly string[] All =
                [Name, Description, Trigger, Limits, Retry, Variables, OutputActions, IndexJs, PackageJson, LockJson];
        }

        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
        };

        private static readonly JsonSerializerOptions Indented = new(Options) { WriteIndented = true };

        /// <summary>The file as downloaded: indented, so a diff in a pull request is readable.</summary>
        public static string Serialize(FunctionExportFile file) => JsonSerializer.Serialize(file, Indented);

        /// <summary>
        /// Reads an uploaded file. Throws <see cref="FunctionValidationException"/> for anything that is not a JSON object
        /// of this shape, or that is over <see cref="MaxImportBytes"/>.
        /// </summary>
        public static FunctionExportFile Parse(JsonElement file)
        {
            if (file.ValueKind != JsonValueKind.Object)
            {
                throw new FunctionValidationException("The import file must be a JSON object (a function export).");
            }

            var raw = file.GetRawText();
            if (Encoding.UTF8.GetByteCount(raw) > MaxImportBytes)
            {
                throw new FunctionValidationException($"The import file is larger than {MaxImportBytes / (1024 * 1024)} MB.");
            }

            try
            {
                return JsonSerializer.Deserialize<FunctionExportFile>(raw, Options)
                    ?? throw new FunctionValidationException("The import file is empty.");
            }
            catch (JsonException ex)
            {
                // The path says where; the message never echoes a value from the file.
                throw new FunctionValidationException(
                    $"The import file is not a valid function export (at {ex.Path ?? "$"}).");
            }
        }

        /// <summary>The canonical JSON of a body: sorted keys at every level, no whitespace.</summary>
        public static string Canonical(FunctionExportBody body)
        {
            ArgumentNullException.ThrowIfNull(body);
            var node = JsonSerializer.SerializeToNode(body, Options);
            return Sort(node)?.ToJsonString(Options) ?? "null";
        }

        /// <summary>The canonical JSON of any value written with <see cref="Options"/> — used for one field's diff.</summary>
        public static string CanonicalOf<T>(T value) =>
            Sort(JsonSerializer.SerializeToNode(value, Options))?.ToJsonString(Options) ?? "null";

        /// <summary>Lower-case hex sha256 over <see cref="Canonical"/>.</summary>
        public static string Hash(FunctionExportBody body) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(body))));

        /// <summary>A readable, indented rendering of one setting for the preview's "mine / incoming" columns.</summary>
        public static string Pretty<T>(T value) =>
            Sort(JsonSerializer.SerializeToNode(value, Options))?.ToJsonString(Indented) ?? "null";

        /// <summary>A deep copy through the canonical serializer, so normalising a body never touches the original.</summary>
        public static FunctionExportBody Copy(FunctionExportBody body) =>
            JsonSerializer.Deserialize<FunctionExportBody>(JsonSerializer.Serialize(body, Options), Options)!;

        /// <summary>
        /// A body in the form an import stores, for comparing: limits clamped, the fixed retry policy, the trigger in its
        /// stored form, install scripts off (an import never turns them on, so that flag never makes two bodies differ).
        /// Returns a copy.
        /// </summary>
        public static FunctionExportBody Normalized(FunctionExportBody body)
        {
            var copy = Copy(body);
            copy.Limits = (copy.Limits ?? new FunctionLimits()).Clamp();
            copy.Retry = RetryPolicy.Fixed;
            copy.Trigger = Services.FunctionService.NormalizeForStorage(copy.Trigger);
            copy.Source ??= new FunctionExportSource();
            copy.Source.AllowInstallScripts = false;
            copy.OutputActions ??= [];
            copy.Variables ??= [];
            return copy;
        }

        /// <summary>
        /// The file name a download is offered under: <c>function-{name}-{id8}.json</c>, the name reduced to
        /// letters, digits and dashes so no header or file system has to escape anything.
        /// </summary>
        public static string FileName(FunctionExportFile file)
        {
            var body = file.Function;
            var name = new string((body?.Name ?? "function")
                .Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
            while (name.Contains("--", StringComparison.Ordinal)) name = name.Replace("--", "-", StringComparison.Ordinal);
            name = name.Trim('-');
            if (name.Length == 0) name = "function";
            if (name.Length > 40) name = name[..40].TrimEnd('-');
            var id = body?.ItemId ?? string.Empty;
            return $"function-{name}-{id[..Math.Min(8, id.Length)]}.json";
        }

        /// <summary>
        /// A function id an import may create: a GUID in its canonical lower-case form — exactly what
        /// <c>FunctionService.CreateAsync</c> issues. The id becomes a Mongo <c>_id</c>, a URL segment and an image
        /// repository name, so nothing looser is accepted.
        /// </summary>
        public static bool IsValidItemId(string? itemId) =>
            !string.IsNullOrEmpty(itemId)
            && Guid.TryParseExact(itemId, "D", out var parsed)
            && string.Equals(parsed.ToString("D"), itemId, StringComparison.Ordinal);

        private static JsonNode? Sort(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    var sorted = new JsonObject();
                    foreach (var (key, value) in obj.OrderBy(p => p.Key, StringComparer.Ordinal).ToList())
                    {
                        obj.Remove(key);
                        sorted[key] = Sort(value);
                    }
                    return sorted;
                case JsonArray array:
                    var copy = new JsonArray();
                    foreach (var item in array.ToList())
                    {
                        array.Remove(item);
                        copy.Add(Sort(item));
                    }
                    return copy;
                default:
                    return node;
            }
        }
    }
}

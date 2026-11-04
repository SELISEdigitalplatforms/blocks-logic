using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using MimeKit;
using Microsoft.Net.Http.Headers;
using MongoDB.Bson;

namespace Workflow.DomainService.Services
{
    /// <summary>A file part or binary body waiting to be stored. Owns its temporary file, if any.</summary>
    public sealed class WebhookPendingFile : IDisposable
    {
        public required string Field { get; init; }
        public required string Name { get; init; }
        public required string ContentType { get; init; }
        public required IFormFile File { get; init; }
        public long Length => File.Length;

        /// <summary>Set for a raw binary body, which the reader streamed to disk; deleted on dispose.</summary>
        public string? TempPath { get; init; }

        /// <summary>The open stream over <see cref="TempPath"/>, closed before the file is deleted.</summary>
        public Stream? TempStream { get; init; }

        public void Dispose()
        {
            TempStream?.Dispose();
            if (TempPath is not null) WebhookContentReader.TryDeleteTempFile(TempPath);
        }
    }

    /// <summary>What an Any content request carried, before any file is stored.</summary>
    public sealed class WebhookContent : IDisposable
    {
        public required string BodyFormat { get; init; }
        public required string ContentType { get; init; }

        /// <summary>One body per item: one for every format except a JSON array (one per element, none for []).</summary>
        public required IReadOnlyList<BsonValue> Bodies { get; init; }
        public IReadOnlyList<WebhookPendingFile> Files { get; init; } = [];
        public required BsonDocument Headers { get; init; }
        public required BsonDocument Query { get; init; }
        public long Bytes { get; init; }

        public void Dispose()
        {
            foreach (var file in Files) file.Dispose();
        }
    }

    /// <summary>
    /// Reads a webhook request body. Only ever called after the trigger's auth has passed. Legacy mode keeps the
    /// JSON-only contract; Any content mode maps every supported content type to JSON bodies plus pending files
    /// (SPEC8 § Any-content mode). Messages never echo body content.
    /// </summary>
    public static class WebhookContentReader
    {
        public const string FormatJson = "json";
        public const string FormatForm = "form";
        public const string FormatMultipart = "multipart";
        public const string FormatText = "text";
        public const string FormatBinary = "binary";
        public const string FormatEmpty = "empty";

        /// <summary>Kestrel's default body limit, which was the legacy webhook limit before the routes were raised.</summary>
        public const long LegacyMaxBytes = 30_000_000;

        private const string OctetStream = "application/octet-stream";
        private const string MessageNonFileTooLarge = "Request body exceeds 5 MiB";
        private const string MessageFileRequestTooLarge = "Request body exceeds 50 MiB";
        private const string MessageInvalidJson = "Body is not valid JSON";
        private const int CopyBufferSize = 81920;

        // ---------------------------------------------------------------- legacy

        /// <summary>
        /// The legacy body: JSON only, as the former <c>[FromBody] JsonElement</c> binding accepted it (415 for
        /// another or missing Content-Type, 400 for an empty or malformed body).
        /// </summary>
        public static async Task<JsonElement> ReadLegacyJsonAsync(HttpRequest request, CancellationToken ct = default)
        {
            var mediaType = MediaTypeOf(request, out _);
            if (!IsJson(mediaType))
            {
                throw WebhookIntakeException.Unsupported("Content-Type must be application/json");
            }

            if (request.ContentLength > LegacyMaxBytes)
            {
                throw WebhookIntakeException.TooLarge("Request body is too large");
            }

            var bytes = await ReadLimitedAsync(request.Body, LegacyMaxBytes, "Request body is too large", ct);
            if (bytes.Length == 0)
            {
                throw WebhookIntakeException.BadRequest("A non-empty request body is required");
            }

            try
            {
                using var document = JsonDocument.Parse(bytes);
                return document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new WebhookIntakeException(400, "invalid_body", MessageInvalidJson, ex);
            }
        }

        // ---------------------------------------------------------------- any content

        public static async Task<WebhookContent> ReadAnyContentAsync(
            HttpRequest request, WebhookIntakeSettings settings, CancellationToken ct = default)
        {
            var mediaType = MediaTypeOf(request, out var parsed);
            var headers = ReadHeaders(request);
            var query = ReadQuery(request);

            WebhookContent Empty() => new()
            {
                BodyFormat = FormatEmpty,
                ContentType = mediaType,
                Bodies = [BsonNull.Value],
                Headers = headers,
                Query = query,
            };

            if (!CanHaveBody(request))
            {
                return Empty();
            }

            var format = EnsureAccepted(mediaType, settings);
            return format switch
            {
                FormatMultipart => await ReadMultipartAsync(request, parsed!, mediaType, headers, query, settings.FilesAllowed, ct),
                FormatForm => await ReadUrlEncodedAsync(request, mediaType, headers, query, ct),
                FormatJson or FormatText => await ReadJsonOrTextAsync(request, parsed, mediaType, headers, query, Empty, ct),
                _ => await ReadBinaryAsync(request, mediaType, headers, query, Empty, ct),
            };
        }

        /// <summary>
        /// The refusals decided by the declared type alone, before a byte of the body is read: XML, a type outside
        /// the webhook's Accepted Body Types (SPEC10 K9), and a binary body without an authenticated caller (K1).
        /// Returns the body format.
        /// </summary>
        private static string EnsureAccepted(string mediaType, WebhookIntakeSettings settings)
        {
            if (IsXml(mediaType))
            {
                throw WebhookIntakeException.Unsupported("XML bodies are not supported yet");
            }

            var format = FormatOf(mediaType);
            if (!settings.Accepts(format))
            {
                throw WebhookIntakeException.TypeNotAccepted(format);
            }
            if (format == FormatBinary && !settings.FilesAllowed)
            {
                throw WebhookIntakeException.FilesNotAllowed();
            }
            return format;
        }

        /// <summary>A JSON or <c>text/*</c> body; which of the two follows from <paramref name="mediaType"/>.</summary>
        private static async Task<WebhookContent> ReadJsonOrTextAsync(
            HttpRequest request, MediaTypeHeaderValue? parsed, string mediaType,
            BsonDocument headers, BsonDocument query, Func<WebhookContent> empty, CancellationToken ct)
        {
            if (request.ContentLength > WebhookIntakeLimits.NonFileMaxBytes)
            {
                throw WebhookIntakeException.TooLarge(MessageNonFileTooLarge);
            }

            var isJson = IsJson(mediaType);
            var format = isJson ? FormatJson : FormatText;
            // Resolve the charset before reading, so an unusable one is refused without consuming the body.
            var encoding = isJson ? Encoding.UTF8 : ResolveEncoding(parsed);
            var bytes = await ReadLimitedAsync(request.Body, WebhookIntakeLimits.NonFileMaxBytes, MessageNonFileTooLarge, ct);
            if (bytes.Length == 0) return empty();

            return new WebhookContent
            {
                BodyFormat = format,
                ContentType = mediaType,
                Bodies = isJson ? ParseJsonBodies(bytes) : [new BsonString(encoding.GetString(bytes))],
                Headers = headers,
                Query = query,
                Bytes = bytes.Length,
            };
        }

        private static async Task<WebhookContent> ReadUrlEncodedAsync(
            HttpRequest request, string mediaType, BsonDocument headers, BsonDocument query, CancellationToken ct)
        {
            if (request.ContentLength > WebhookIntakeLimits.NonFileMaxBytes)
            {
                throw WebhookIntakeException.TooLarge(MessageNonFileTooLarge);
            }

            var form = await ReadFormAsync(request, MessageNonFileTooLarge, ct);
            var (fields, fieldBytes) = ReadFields(form);
            if (fieldBytes > WebhookIntakeLimits.NonFileMaxBytes)
            {
                throw WebhookIntakeException.TooLarge(MessageNonFileTooLarge);
            }

            return new WebhookContent
            {
                BodyFormat = FormatForm,
                ContentType = mediaType,
                Bodies = [fields],
                Headers = headers,
                Query = query,
                Bytes = request.ContentLength ?? fieldBytes,
            };
        }

        private static async Task<WebhookContent> ReadMultipartAsync(
            HttpRequest request, MediaTypeHeaderValue parsed, string mediaType,
            BsonDocument headers, BsonDocument query, bool filesAllowed, CancellationToken ct)
        {
            if (HeaderUtilities.RemoveQuotes(parsed.Boundary).Length == 0)
            {
                throw WebhookIntakeException.BadRequest("Multipart body has no boundary");
            }

            if (request.ContentLength > WebhookIntakeLimits.FileRequestMaxBytes)
            {
                throw WebhookIntakeException.TooLarge(MessageFileRequestTooLarge);
            }

            // ReadFormAsync, not a hand-rolled MultipartReader: Genesis may already have read the form while
            // resolving the tenant for a bearer token, and the form feature caches that result. Parts above
            // FormOptions.MemoryBufferThreshold (64 KiB) are buffered to disk, not memory.
            var form = await ReadFormAsync(request, MessageFileRequestTooLarge, ct);

            if (form.Files.Count > WebhookIntakeLimits.MaxFileParts)
            {
                throw WebhookIntakeException.TooLarge($"Too many files (max {WebhookIntakeLimits.MaxFileParts})");
            }

            var (fields, fieldBytes) = ReadFields(form);
            if (fieldBytes > WebhookIntakeLimits.NonFileMaxBytes)
            {
                throw WebhookIntakeException.TooLarge("Form fields exceed 5 MiB");
            }

            var fileBytes = form.Files.Sum(f => f.Length);
            var total = fieldBytes + fileBytes;
            if (total > WebhookIntakeLimits.FileRequestMaxBytes)
            {
                throw WebhookIntakeException.TooLarge(MessageFileRequestTooLarge);
            }
            if (form.Files.Count == 0 && total > WebhookIntakeLimits.NonFileMaxBytes)
            {
                throw WebhookIntakeException.TooLarge(MessageNonFileTooLarge);
            }

            // Checked after the size limits so that an oversized request still gets 413, as SPEC10 K7 requires.
            // Nothing is stored at this point, and the buffered form parts are removed with the request.
            if (form.Files.Count > 0 && !filesAllowed)
            {
                throw WebhookIntakeException.FilesNotAllowed();
            }

            var files = form.Files
                .Select(file =>
                {
                    var partType = string.IsNullOrWhiteSpace(file.ContentType) ? OctetStream : file.ContentType;
                    return new WebhookPendingFile
                    {
                        Field = file.Name,
                        Name = EnsureExtension(SanitizeFileName(file.FileName), partType),
                        ContentType = partType,
                        File = file,
                    };
                })
                .ToList();

            return new WebhookContent
            {
                BodyFormat = FormatMultipart,
                ContentType = mediaType,
                Bodies = [fields],
                Files = files,
                Headers = headers,
                Query = query,
                Bytes = request.ContentLength ?? total,
            };
        }

        private static async Task<WebhookContent> ReadBinaryAsync(
            HttpRequest request, string mediaType, BsonDocument headers, BsonDocument query,
            Func<WebhookContent> empty, CancellationToken ct)
        {
            if (request.ContentLength > WebhookIntakeLimits.FileRequestMaxBytes)
            {
                throw WebhookIntakeException.TooLarge(MessageFileRequestTooLarge);
            }

            // Streamed to a temporary file so a 50 MiB body never sits in memory (C15).
            var tempPath = Path.Combine(Path.GetTempPath(), $"blocks-webhook-{Guid.NewGuid():N}.tmp");
            long length = 0;
            try
            {
                await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 CopyBufferSize, FileOptions.Asynchronous))
                {
                    var buffer = new byte[CopyBufferSize];
                    int read;
                    while ((read = await ReadBodyAsync(request.Body, buffer, MessageFileRequestTooLarge, ct)) > 0)
                    {
                        length += read;
                        if (length > WebhookIntakeLimits.FileRequestMaxBytes)
                        {
                            throw WebhookIntakeException.TooLarge(MessageFileRequestTooLarge);
                        }
                        await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                }
            }
            catch
            {
                TryDeleteTempFile(tempPath);
                throw;
            }

            if (length == 0)
            {
                TryDeleteTempFile(tempPath);
                return empty();
            }

            var contentType = mediaType.Length > 0 ? mediaType : OctetStream;
            var name = EnsureExtension(SanitizeFileName(FileNameFromDisposition(request)), contentType);
            var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                CopyBufferSize, FileOptions.Asynchronous);
            var file = new FormFile(stream, 0, length, "body", name)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType,
            };

            return new WebhookContent
            {
                BodyFormat = FormatBinary,
                ContentType = mediaType,
                Bodies = [BsonNull.Value],
                Files =
                [
                    new WebhookPendingFile
                    {
                        Field = "body",
                        Name = name,
                        ContentType = contentType,
                        File = file,
                        TempPath = tempPath,
                        TempStream = stream,
                    },
                ],
                Headers = headers,
                Query = query,
                Bytes = length,
            };
        }

        // ---------------------------------------------------------------- helpers

        private static string MediaTypeOf(HttpRequest request, out MediaTypeHeaderValue? parsed)
        {
            parsed = null;
            var raw = request.ContentType;
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            if (!MediaTypeHeaderValue.TryParse(raw, out parsed))
            {
                throw WebhookIntakeException.Unsupported("Content-Type could not be read");
            }
            return parsed.MediaType.Value?.ToLowerInvariant() ?? string.Empty;
        }

        /// <summary>
        /// The body format a media type maps to (SPEC8 § mapping), decided before the body is read. A JSON or text
        /// body that turns out to be empty is still reported as empty once read.
        /// </summary>
        public static string FormatOf(string mediaType) => mediaType switch
        {
            "multipart/form-data" => FormatMultipart,
            "application/x-www-form-urlencoded" => FormatForm,
            _ when IsJson(mediaType) => FormatJson,
            _ when mediaType.StartsWith("text/", StringComparison.Ordinal) => FormatText,
            _ => FormatBinary,
        };

        /// <summary>
        /// False when the request certainly has no body: Content-Length 0, or, on a server request, Kestrel's own
        /// body detection (no Content-Length and no chunked encoding). Unknown means a body may follow.
        /// </summary>
        private static bool CanHaveBody(HttpRequest request)
        {
            if (request.ContentLength == 0) return false;
            if (request.ContentLength > 0) return true;
            return request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody ?? true;
        }

        private static bool IsJson(string mediaType) =>
            mediaType is "application/json" or "text/json" || mediaType.EndsWith("+json", StringComparison.Ordinal);

        private static bool IsXml(string mediaType) =>
            mediaType is "application/xml" or "text/xml" || mediaType.EndsWith("+xml", StringComparison.Ordinal);

        private static Encoding ResolveEncoding(MediaTypeHeaderValue? parsed)
        {
            var charset = HeaderUtilities.RemoveQuotes(parsed?.Charset ?? default).ToString();
            if (string.IsNullOrWhiteSpace(charset)) return Encoding.UTF8;
            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                throw WebhookIntakeException.Unsupported("Unsupported charset");
            }
        }

        private static IReadOnlyList<BsonValue> ParseJsonBodies(byte[] bytes)
        {
            try
            {
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                return root.ValueKind == JsonValueKind.Array
                    ? root.EnumerateArray().Select(ToBson).ToList()
                    : [ToBson(root)];
            }
            catch (JsonException ex)
            {
                throw new WebhookIntakeException(400, "invalid_body", MessageInvalidJson, ex);
            }
        }

        /// <summary>Any JSON value (primitives and arrays too), via a one-field wrapper document.</summary>
        private static BsonValue ToBson(JsonElement element)
        {
            try
            {
                return BsonDocument.Parse("{\"v\":" + element.GetRawText() + "}")["v"];
            }
            catch (Exception ex) when (ex is FormatException or BsonSerializationException or InvalidOperationException)
            {
                // e.g. a "$date"/"$oid" style key that Mongo's extended JSON reader rejects.
                throw new WebhookIntakeException(400, "invalid_body", MessageInvalidJson, ex);
            }
        }

        private static async Task<IFormCollection> ReadFormAsync(HttpRequest request, string tooLargeMessage, CancellationToken ct)
        {
            try
            {
                return await request.ReadFormAsync(ct);
            }
            catch (InvalidDataException ex)
            {
                // FormOptions limits (multipart body length, value length, value count).
                throw new WebhookIntakeException(413, "too_large", tooLargeMessage, ex);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                throw new WebhookIntakeException(413, "too_large", tooLargeMessage, ex);
            }
        }

        private static (BsonDocument Fields, long Bytes) ReadFields(IFormCollection form)
        {
            var fields = new BsonDocument();
            long bytes = 0;
            foreach (var (key, values) in form)
            {
                bytes += Encoding.UTF8.GetByteCount(key);
                foreach (var value in values) bytes += Encoding.UTF8.GetByteCount(value ?? string.Empty);
                fields[key] = values.Count == 1
                    ? new BsonString(values[0] ?? string.Empty)
                    : new BsonArray(values.Select(v => new BsonString(v ?? string.Empty)));
            }
            return (fields, bytes);
        }

        /// <summary>Every request header except the excluded ones, under lower-cased names (SPEC10 N4).</summary>
        private static BsonDocument ReadHeaders(HttpRequest request)
        {
            var headers = new BsonDocument();
            foreach (var (name, values) in request.Headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (values.Count == 0 || WebhookHeaderFilter.IsExcluded(name)) continue;
                headers[name.ToLowerInvariant()] = string.Join(", ", values.ToArray());
            }
            return headers;
        }

        private static BsonDocument ReadQuery(HttpRequest request)
        {
            var query = new BsonDocument();
            foreach (var (key, values) in request.Query)
            {
                query[key] = values.Count == 1
                    ? new BsonString(values[0] ?? string.Empty)
                    : new BsonArray(values.Select(v => new BsonString(v ?? string.Empty)));
            }
            return query;
        }

        private static string? FileNameFromDisposition(HttpRequest request)
        {
            var raw = request.Headers.ContentDisposition.ToString();
            if (string.IsNullOrWhiteSpace(raw) || !ContentDispositionHeaderValue.TryParse(raw, out var disposition))
            {
                return null;
            }
            var name = disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName;
            return HeaderUtilities.RemoveQuotes(name).ToString();
        }

        /// <summary>Path segments and control characters removed, at most 255 characters, "upload" when empty (C13).</summary>
        public static string SanitizeFileName(string? name)
        {
            var candidate = name ?? string.Empty;
            var lastSeparator = candidate.LastIndexOfAny(['/', '\\']);
            if (lastSeparator >= 0) candidate = candidate[(lastSeparator + 1)..];
            candidate = new string(candidate.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (candidate.Length > WebhookIntakeLimits.MaxFileNameLength)
            {
                candidate = candidate[..WebhookIntakeLimits.MaxFileNameLength];
            }
            return candidate.Length == 0 ? "upload" : candidate;
        }

        /// <summary>
        /// Storage refuses a file name without an extension, so a name that has none (for example the "upload"
        /// fallback of a raw binary body) gets one from its content type, or ".bin" when the type is unknown (C13).
        /// The result stays within <see cref="WebhookIntakeLimits.MaxFileNameLength"/>.
        /// </summary>
        public static string EnsureExtension(string name, string? contentType)
        {
            var trimmed = name.TrimEnd('.', ' ');
            if (trimmed.Length == 0) trimmed = "upload";
            if (Path.HasExtension(trimmed)) return trimmed;

            var mediaType = (contentType ?? string.Empty).Split(';')[0].Trim();
            var extension = ".bin";
            if (mediaType.Length > 0 && MimeTypes.TryGetExtension(mediaType, out var known) && !string.IsNullOrEmpty(known))
            {
                extension = known.StartsWith('.') ? known : "." + known;
            }

            var maxBase = WebhookIntakeLimits.MaxFileNameLength - extension.Length;
            if (trimmed.Length > maxBase) trimmed = trimmed[..maxBase];
            return trimmed + extension;
        }

        private static async Task<byte[]> ReadLimitedAsync(Stream body, long max, string tooLargeMessage, CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[CopyBufferSize];
            int read;
            while ((read = await ReadBodyAsync(body, chunk, tooLargeMessage, ct)) > 0)
            {
                if (buffer.Length + read > max)
                {
                    throw WebhookIntakeException.TooLarge(tooLargeMessage);
                }
                await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
            }
            return buffer.ToArray();
        }

        /// <summary>One read, turning Kestrel's own body-size refusal into the intake's 413.</summary>
        private static async Task<int> ReadBodyAsync(Stream body, byte[] buffer, string tooLargeMessage, CancellationToken ct)
        {
            try
            {
                return await body.ReadAsync(buffer, ct);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                throw new WebhookIntakeException(413, "too_large", tooLargeMessage, ex);
            }
        }

        /// <summary>Deletes a temporary body file; failing to is not an error for the request.</summary>
        internal static void TryDeleteTempFile(string path)
        {
            try
            {
                System.IO.File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort: the request's outcome does not depend on it, and the OS cleans its temp folder.
            }
        }
    }
}

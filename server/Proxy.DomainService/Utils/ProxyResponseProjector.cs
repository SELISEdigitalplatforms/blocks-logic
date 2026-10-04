using System.Buffers;
using System.Text.Json;
using Proxy.DomainService.Entities;

namespace Proxy.DomainService.Utils
{
    /// <summary>What a response filter did to the upstream body (mirrored onto the execution row).</summary>
    public enum ProxyResponseFilterNote
    {
        /// <summary><see cref="ProxyResponseMode.All"/> &mdash; the filter never ran.</summary>
        NotConfigured,

        /// <summary>The body was projected to a non-empty structural subset.</summary>
        Applied,

        /// <summary>The projection produced <c>{}</c> / <c>[]</c>, or the body was empty, or nothing matched.</summary>
        EmptyResult,

        /// <summary>The 2xx JSON body was a primitive or an array of only primitives &mdash; relayed whole.</summary>
        WholePrimitive,

        /// <summary>The body could not be projected (non-2xx, non-JSON, unparseable, or over the 5 MB limit).</summary>
        Failed,
    }

    /// <summary>
    /// Projects a third party's JSON response body to a proxy's configured <c>ResponseInclude</c> path set
    /// (SPEC "response field filtering" &sect;2.3). Pure, synchronous, no IO; never throws (every
    /// <see cref="JsonException"/> is caught and mapped to <see cref="ProxyResponseFilterNote.Failed"/>).
    /// <para>
    /// The projection is a <em>structural subset</em>: the output has the same shape as the input (same
    /// nesting, key names, array positions) with unselected branches removed. No renaming, hoisting,
    /// flattening, or computed values.
    /// </para>
    /// </summary>
    public static class ProxyResponseProjector
    {
        /// <summary>Raw upstream bodies larger than this are failed rather than projected (fail-closed).</summary>
        public const long MaxProjectableBytes = 5L * 1024 * 1024;

        private const int MaxJsonDepth = 128;

        private const string JsonContentType = "application/json; charset=utf-8";

        private static readonly JsonWriterOptions WriterOptions = new() { SkipValidation = true };

        private readonly record struct PathCursor(ProxyResponsePath.Segment[] Path, int Pos);

        /// <summary>
        /// Applies the forward-time policy of SPEC &sect;2.2 / &sect;2.3. Returns the body to relay (or
        /// <c>null</c> on a <see cref="ProxyResponseFilterNote.Failed"/>), the Content-Type to relay, the
        /// note to record, and &mdash; on a failure &mdash; a short reason that carries no upstream body text.
        /// </summary>
        public static (byte[]? Body, string? ContentType, ProxyResponseFilterNote Note, string? FailReason) Project(
            byte[]? body,
            string? contentType,
            int upstreamStatus,
            ProxyResponseMode mode,
            IReadOnlyList<string> include)
        {
            // 1 — default path: zero overhead, no behaviour change.
            if (mode == ProxyResponseMode.All)
            {
                return (body, contentType, ProxyResponseFilterNote.NotConfigured, null);
            }

            // 2 — a non-2xx upstream is never filtered; the call fails closed.
            if (upstreamStatus is < 200 or > 299)
            {
                return (null, null, ProxyResponseFilterNote.Failed, $"upstream returned {upstreamStatus}");
            }

            // 3 — must be JSON by media type.
            if (!IsJsonMediaType(contentType, out var mediaTypeOrNone))
            {
                return (null, null, ProxyResponseFilterNote.Failed,
                    $"upstream response was not JSON ({mediaTypeOrNone})");
            }

            // 4 — an empty body is relayed empty.
            if (body is null || body.Length == 0)
            {
                return (body ?? Array.Empty<byte>(), contentType, ProxyResponseFilterNote.EmptyResult, null);
            }

            // 5 — oversized bodies are failed without a parse attempt.
            if (body.LongLength > MaxProjectableBytes)
            {
                return (null, null, ProxyResponseFilterNote.Failed,
                    $"response body is {body.LongLength} bytes, over the 5 MB filtering limit");
            }

            // 6 — parse, bounded depth.
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            }
            catch (JsonException)
            {
                return (null, null, ProxyResponseFilterNote.Failed, "upstream response could not be parsed as JSON");
            }

            using (doc)
            {
                var root = doc.RootElement;

                // 7 — nothing to prune: a primitive root, or an array of only primitives (empty array counts).
                if (IsWholePrimitive(root))
                {
                    return (body, contentType, ProxyResponseFilterNote.WholePrimitive, null);
                }

                // 8 — an empty include list relays {} / [].
                if (include.Count == 0)
                {
                    return (EmptyRoot(root.ValueKind), JsonContentType, ProxyResponseFilterNote.EmptyResult, null);
                }

                // 9 — parse each path once, dedupe, then walk.
                var cursors = BuildCursors(include);
                if (cursors.Count == 0)
                {
                    return (EmptyRoot(root.ValueKind), JsonContentType, ProxyResponseFilterNote.EmptyResult, null);
                }

                var buffer = new ArrayBufferWriter<byte>();
                using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
                {
                    WriteProjectedNode(writer, root, cursors);
                }

                var output = buffer.WrittenSpan.ToArray();

                // 10 — an all-pruned root ({} / []) records EmptyResult, otherwise Applied.
                var note = IsEmptyStructure(output)
                    ? ProxyResponseFilterNote.EmptyResult
                    : ProxyResponseFilterNote.Applied;
                return (output, JsonContentType, note, null);
            }
        }

        /// <summary>
        /// Writes <paramref name="node"/>'s projected form. The caller has already decided the node itself is
        /// kept (a selected leaf, an intermediate container on a selected path, or an array element whose
        /// position is preserved). Implements the keep rules M1&ndash;M5.
        /// </summary>
        private static void WriteProjectedNode(Utf8JsonWriter writer, JsonElement node, List<PathCursor> state)
        {
            // M1 — a terminal match emits the whole subtree verbatim.
            foreach (var cursor in state)
            {
                if (cursor.Pos == cursor.Path.Length)
                {
                    node.WriteTo(writer);
                    return;
                }
            }

            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in node.EnumerateObject())
                    {
                        var next = NextCursors(state, property.Name);
                        if (next.Count == 0)
                        {
                            continue; // M2 — property not on any selected path
                        }

                        if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        {
                            writer.WritePropertyName(property.Name);
                            WriteProjectedNode(writer, property.Value, next); // M2 — kept even if it emits {} / []
                        }
                        else if (HasTerminal(next))
                        {
                            // The property IS the selected leaf: keep the primitive as-is.
                            writer.WritePropertyName(property.Name);
                            property.Value.WriteTo(writer);
                        }

                        // else M5 — primitive value with more path expected: dead-end, silently skipped.
                    }

                    writer.WriteEndObject();
                    return;

                case JsonValueKind.Array:
                    // M3 — same length; recurse into each element with the same state.
                    writer.WriteStartArray();
                    foreach (var element in node.EnumerateArray())
                    {
                        if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        {
                            WriteProjectedNode(writer, element, state);
                        }
                        else
                        {
                            element.WriteTo(writer); // primitive element: no sub-structure to prune
                        }
                    }

                    writer.WriteEndArray();
                    return;

                default:
                    // A primitive with only non-terminal cursors: unreachable from the object branch (M5 handles
                    // it there) and from the array branch; guarded here so the walk never throws.
                    writer.WriteNullValue();
                    return;
            }
        }

        private static List<PathCursor> NextCursors(List<PathCursor> state, string key)
        {
            var next = new List<PathCursor>();
            foreach (var cursor in state)
            {
                if (cursor.Pos < cursor.Path.Length
                    && string.Equals(cursor.Path[cursor.Pos].Key, key, StringComparison.Ordinal))
                {
                    next.Add(cursor with { Pos = cursor.Pos + 1 });
                }
            }

            return next;
        }

        private static bool HasTerminal(List<PathCursor> cursors)
        {
            foreach (var cursor in cursors)
            {
                if (cursor.Pos == cursor.Path.Length)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<PathCursor> BuildCursors(IReadOnlyList<string> include)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var cursors = new List<PathCursor>(include.Count);
            foreach (var raw in include)
            {
                if (string.IsNullOrEmpty(raw) || !seen.Add(raw) || !ProxyResponsePath.IsValid(raw))
                {
                    continue;
                }

                cursors.Add(new PathCursor(ProxyResponsePath.Parse(raw), 0));
            }

            return cursors;
        }

        private static bool IsWholePrimitive(JsonElement root)
        {
            switch (root.ValueKind)
            {
                case JsonValueKind.String:
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                    return true;
                case JsonValueKind.Array:
                    foreach (var element in root.EnumerateArray())
                    {
                        if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        {
                            return false;
                        }
                    }

                    return true;
                default:
                    return false;
            }
        }

        private static byte[] EmptyRoot(JsonValueKind rootKind) =>
            rootKind == JsonValueKind.Array ? "[]"u8.ToArray() : "{}"u8.ToArray();

        private static bool IsEmptyStructure(byte[] output)
        {
            var text = System.Text.Encoding.UTF8.GetString(output).AsSpan().Trim();
            return text is "{}" or "[]";
        }

        /// <summary>
        /// <c>true</c> when <paramref name="contentType"/>'s media type is <c>application/json</c> or ends
        /// <c>+json</c> (case-insensitive, parameters ignored). <paramref name="mediaTypeOrNone"/> is the bare
        /// media type, or <c>"none"</c> when there is no usable Content-Type.
        /// </summary>
        private static bool IsJsonMediaType(string? contentType, out string mediaTypeOrNone)
        {
            mediaTypeOrNone = "none";
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return false;
            }

            var semicolon = contentType.IndexOf(';');
            var media = (semicolon >= 0 ? contentType[..semicolon] : contentType).Trim();
            if (media.Length == 0)
            {
                return false;
            }

            mediaTypeOrNone = media;
            return media.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || media.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
        }
    }
}

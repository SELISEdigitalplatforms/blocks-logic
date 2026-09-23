using System.Text.Json;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Utils;

namespace Workflow.DomainService.Import
{
    public sealed class WorkflowImportPreflightResult
    {
        public bool Ok { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public JsonElement Root { get; init; }
    }

    public sealed class RemappedWorkflow
    {
        public List<NodeEntity> Nodes { get; init; } = new();
        public List<EdgeEnity> Edges { get; init; } = new();
        public Dictionary<string, string> Settings { get; init; } = new();
        public int Issues { get; init; }
    }

    public static class WorkflowImportMapper
    {
        public const int MaxImportBytes = 5 * 1024 * 1024;

        public const string ImportTooLarge = "IMPORT_TOO_LARGE";
        public const string ImportNotJson = "IMPORT_NOT_JSON";
        public const string ImportBadShape = "IMPORT_BAD_SHAPE";

        public static readonly string ImportTooLargeMessage = "This file is larger than the 5 MB limit.";
        public static readonly string ImportNotJsonMessage = "This file is not valid JSON.";
        public static readonly string ImportBadShapeMessage =
            "This file is not a valid workflow export (missing name, nodes, edges or settings).";

        private static readonly HashSet<string> NodesRequiringProjectKey = new(StringComparer.OrdinalIgnoreCase)
        {
            "dataAction",
            "dataGateway",
            "sendMail",
        };

        public static WorkflowImportPreflightResult Preflight(string text, long size)
        {
            if (size > MaxImportBytes)
            {
                return Fail(ImportTooLarge, ImportTooLargeMessage);
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                return Fail(ImportNotJson, ImportNotJsonMessage);
            }

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail(ImportNotJson, ImportNotJsonMessage);
            }

            if (!TryGetNonEmptyString(root, "name", out _))
            {
                return Fail(ImportBadShape, ImportBadShapeMessage);
            }

            if (!IsArray(root, "nodes") || !IsArray(root, "edges") || !IsObject(root, "settings"))
            {
                return Fail(ImportBadShape, ImportBadShapeMessage);
            }

            return new WorkflowImportPreflightResult { Ok = true, Root = root.Clone() };
        }

        public static string GetName(JsonElement root)
        {
            return root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString() ?? string.Empty
                : string.Empty;
        }

        public static string ReplaceIdsInString(string value, IReadOnlyDictionary<string, string> idMap)
        {
            var output = value;
            foreach (var (oldId, newId) in idMap)
            {
                if (string.IsNullOrEmpty(oldId) || oldId == newId || !output.Contains(oldId, StringComparison.Ordinal))
                {
                    continue;
                }

                var result = new System.Text.StringBuilder();
                var from = 0;
                var idx = output.IndexOf(oldId, from, StringComparison.Ordinal);
                while (idx != -1)
                {
                    var before = idx > 0 ? output[idx - 1] : '\0';
                    var afterIdx = idx + oldId.Length;
                    var after = afterIdx < output.Length ? output[afterIdx] : '\0';
                    var isWholeToken = !IsTokenChar(before) && !IsTokenChar(after);
                    result.Append(output.AsSpan(from, idx - from));
                    result.Append(isWholeToken ? newId : oldId);
                    from = afterIdx;
                    idx = output.IndexOf(oldId, from, StringComparison.Ordinal);
                }

                result.Append(output.AsSpan(from));
                output = result.ToString();
            }

            return output;
        }

        public static RemappedWorkflow RemapAndSanitise(JsonElement root)
        {
            var issues = 0;
            var nodesElement = root.GetProperty("nodes");
            var valid = new List<JsonElement>();
            foreach (var node in nodesElement.EnumerateArray())
            {
                if (IsValidImportNode(node))
                {
                    valid.Add(node);
                }
                else
                {
                    issues += 1;
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var survivors = new List<JsonElement>();
            foreach (var node in valid)
            {
                var id = node.GetProperty("id").GetString()!;
                if (!seen.Add(id))
                {
                    issues += 1;
                    continue;
                }

                survivors.Add(node);
            }

            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var node in survivors)
            {
                idMap[node.GetProperty("id").GetString()!] = Guid.NewGuid().ToString("N");
            }

            var nodes = survivors.Select(node => RewriteNode(node, idMap)).ToList();

            var settings = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("settings", out var settingsElement) && settingsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in settingsElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        settings[property.Name] = ReplaceIdsInString(property.Value.GetString() ?? string.Empty, idMap);
                    }
                }
            }

            var edges = new List<EdgeEnity>();
            var usedEdgeIds = new HashSet<string>(StringComparer.Ordinal);
            var edgeIndex = 0;
            if (root.TryGetProperty("edges", out var edgesElement) && edgesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var edge in edgesElement.EnumerateArray())
                {
                    var currentIndex = edgeIndex++;
                    if (edge.ValueKind != JsonValueKind.Object)
                    {
                        issues += 1;
                        continue;
                    }

                    var source = edge.TryGetProperty("source", out var sourceEl) && sourceEl.ValueKind == JsonValueKind.String
                        ? sourceEl.GetString()
                        : null;
                    var target = edge.TryGetProperty("target", out var targetEl) && targetEl.ValueKind == JsonValueKind.String
                        ? targetEl.GetString()
                        : null;
                    if (source is null || target is null
                        || !idMap.TryGetValue(source, out var newSource)
                        || !idMap.TryGetValue(target, out var newTarget))
                    {
                        issues += 1;
                        continue;
                    }

                    var id = $"xy-edge__{newSource}-{newTarget}";
                    if (!usedEdgeIds.Add(id))
                    {
                        id = $"{id}-{currentIndex}";
                        usedEdgeIds.Add(id);
                    }

                    edges.Add(new EdgeEnity
                    {
                        Id = id,
                        Source = newSource,
                        Target = newTarget,
                        SourceHandle = ReadString(edge, "sourceHandle"),
                        TargetHandle = ReadString(edge, "targetHandle"),
                    });
                }
            }

            return new RemappedWorkflow
            {
                Nodes = nodes,
                Edges = edges,
                Settings = settings,
                Issues = issues,
            };
        }

        public static void RewriteProjectIdentity(IEnumerable<NodeEntity> nodes, string tenantId, string? tenantSlug)
        {
            foreach (var node in nodes)
            {
                node.Parameters ??= new BsonDocument();
                var parameters = node.Parameters;
                var type = node.Type ?? string.Empty;
                var hasCamelProjectKey = HasElement(parameters, "projectKey");
                var hasPascalProjectKey = HasElement(parameters, "ProjectKey");
                if (hasCamelProjectKey || hasPascalProjectKey || NodesRequiringProjectKey.Contains(type))
                {
                    parameters["projectKey"] = tenantId;
                }

                if (hasPascalProjectKey)
                {
                    parameters["ProjectKey"] = tenantId;
                }

                if (!string.IsNullOrEmpty(tenantSlug))
                {
                    if (HasElement(parameters, "projectShortKey"))
                    {
                        parameters["projectShortKey"] = tenantSlug;
                    }

                    if (HasElement(parameters, "ProjectShortKey"))
                    {
                        parameters["ProjectShortKey"] = tenantSlug;
                    }
                }

                if (type.Equals("sendMail", StringComparison.OrdinalIgnoreCase)
                    && TryGetString(parameters, "Template", out var template)
                    && !string.IsNullOrEmpty(template))
                {
                    parameters["EmailTemplate"] = $"{template}_{tenantId}";
                }
            }
        }

        private static NodeEntity RewriteNode(JsonElement node, IReadOnlyDictionary<string, string> idMap)
        {
            var oldId = node.GetProperty("id").GetString()!;
            var newId = idMap[oldId];
            var position = node.GetProperty("position");
            return new NodeEntity
            {
                Id = newId,
                Name = ReadString(node, "name"),
                Category = ReadString(node, "category"),
                Type = ReadString(node, "type"),
                Version = ReadString(node, "version"),
                Position = new Position
                {
                    X = position.GetProperty("x").GetDouble(),
                    Y = position.GetProperty("y").GetDouble(),
                },
                Handle = ParseHandle(node),
                Parameters = RemapToDocument(node, "parameters", idMap),
                Settings = RemapToDocument(node, "settings", idMap),
                PinData = RemapToArray(node, "pinData", idMap),
            };
        }

        private static BsonDocument RemapToDocument(JsonElement node, string propertyName, IReadOnlyDictionary<string, string> idMap)
        {
            if (!node.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                return new BsonDocument();
            }

            var remapped = ReplaceIdsInString(value.GetRawText(), idMap);
            try
            {
                return BsonDocument.Parse(remapped);
            }
            catch (FormatException)
            {
                return new BsonDocument();
            }
        }

        private static BsonArray? RemapToArray(JsonElement node, string propertyName, IReadOnlyDictionary<string, string> idMap)
        {
            if (!node.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                return null;
            }

            var remapped = ReplaceIdsInString(value.GetRawText(), idMap);
            using var doc = JsonDocument.Parse(remapped);
            return BsonJsonConverter.ToBsonArrayOrNull(doc.RootElement);
        }

        private static Handle? ParseHandle(JsonElement node)
        {
            if (!node.TryGetProperty("handle", out var handle) || handle.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new Handle
            {
                Sources = ReadStringList(handle, "source", "Sources"),
                Targets = ReadStringList(handle, "target", "Targets"),
            };
        }

        private static List<string> ReadStringList(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
                {
                    return value.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString() ?? string.Empty)
                        .ToList();
                }
            }

            return new List<string>();
        }

        private static bool IsValidImportNode(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!IsNonEmptyString(node, "id")
                || !IsNonEmptyString(node, "name")
                || !IsNonEmptyString(node, "type")
                || !IsNonEmptyString(node, "category")
                || !IsNonEmptyString(node, "version"))
            {
                return false;
            }

            if (!node.TryGetProperty("position", out var position) || position.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return IsFiniteNumber(position, "x") && IsFiniteNumber(position, "y");
        }

        private static bool IsFiniteNumber(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out var number)
                && double.IsFinite(number);
        }

        private static bool IsNonEmptyString(JsonElement element, string name)
        {
            return TryGetNonEmptyString(element, name, out _);
        }

        private static bool TryGetNonEmptyString(JsonElement element, string name, out string value)
        {
            value = string.Empty;
            if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = property.GetString() ?? string.Empty;
            return value.Trim().Length > 0;
        }

        private static string ReadString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;
        }

        private static bool IsArray(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Array;
        }

        private static bool IsObject(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Object;
        }

        private static bool IsTokenChar(char c) => char.IsLetterOrDigit(c);

        private static bool HasElement(BsonDocument document, string name) => document.Contains(name);

        private static bool TryGetString(BsonDocument document, string name, out string value)
        {
            value = string.Empty;
            if (!document.TryGetValue(name, out var bsonValue) || !bsonValue.IsString)
            {
                return false;
            }

            value = bsonValue.AsString;
            return true;
        }

        private static WorkflowImportPreflightResult Fail(string code, string message) => new()
        {
            Ok = false,
            Code = code,
            Message = message,
        };
    }
}

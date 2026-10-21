using System.Text.Json;
using MongoDB.Bson;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Utils
{
    /// <summary>
    /// Maps API node DTOs to stored node entities. Shared by Create and Update so both
    /// save the same node shape the same way.
    /// </summary>
    public static class NodeMapper
    {
        public static List<NodeEntity> ToEntities(IEnumerable<NodeDto>? nodes)
        {
            return nodes?.Select(ToEntity).ToList() ?? new List<NodeEntity>();
        }

        public static NodeEntity ToEntity(NodeDto node)
        {
            ArgumentNullException.ThrowIfNull(node);
            return new NodeEntity
            {
                Id = node.Id,
                Name = node.Name,
                Category = node.Category,
                Type = node.Type,
                Version = node.Version,
                Position = node.Position,
                Handle = ToHandle(node.Handle),
                Parameters = ToDocument(node.Parameters, node.Id, "parameters"),
                Settings = ToDocument(node.Settings, node.Id, "settings"),
                PinData = ToArray(node.PinData, node.Id),
            };
        }

        // Copies the node's handle. Null lists become empty and null entries are dropped, so readers that
        // walk the entries (publish copies trigger handles) never meet a null.
        private static Handle? ToHandle(Handle? handle)
        {
            if (handle is null)
            {
                return null;
            }

            return new Handle
            {
                Sources = CopyEntries(handle.Sources),
                Targets = CopyEntries(handle.Targets),
            };
        }

        private static List<Dictionary<string, string>> CopyEntries(List<Dictionary<string, string>>? entries)
        {
            return entries?.Where(entry => entry is not null).Select(entry => new Dictionary<string, string>(entry)).ToList()
                ?? new List<Dictionary<string, string>>();
        }

        // Missing or null becomes an empty document; anything that is not a JSON object is rejected.
        private static BsonDocument ToDocument(JsonElement value, string nodeId, string field)
        {
            if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                return new BsonDocument();
            }

            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new NodeMappingException($"Node '{nodeId}': '{field}' must be a JSON object, but was {value.ValueKind}.");
            }

            try
            {
                return BsonDocument.Parse(value.GetRawText());
            }
            catch (Exception ex)
            {
                throw new NodeMappingException($"Node '{nodeId}': '{field}' could not be stored: {ex.Message}", ex);
            }
        }

        private static BsonArray? ToArray(JsonElement? value, string nodeId)
        {
            try
            {
                return BsonJsonConverter.ToBsonArrayOrNull(value);
            }
            catch (Exception ex)
            {
                throw new NodeMappingException($"Node '{nodeId}': 'pinData' could not be stored: {ex.Message}", ex);
            }
        }
    }

    /// <summary>A node's parameters, settings or pin data could not be converted for storage.</summary>
    public sealed class NodeMappingException : Exception
    {
        public NodeMappingException() { }

        public NodeMappingException(string message) : base(message) { }

        public NodeMappingException(string message, Exception innerException) : base(message, innerException) { }
    }
}

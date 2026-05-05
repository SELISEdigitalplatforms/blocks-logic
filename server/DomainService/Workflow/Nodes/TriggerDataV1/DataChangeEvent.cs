namespace DomainService.Workflow.Nodes.TriggerDataV1
{
    /// <summary>
    /// Operation types for data change events
    /// </summary>
    public enum DataChangeOperation
    {
        Inserted,
        Updated,
        Deleted
    }

    /// <summary>
    /// Represents changed field values during an update operation
    /// </summary>
    public class FieldChange
    {
        public string FieldName { get; set; } = string.Empty;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
    }

    /// <summary>
    /// Represents a single document's updated fields during an update operation.
    /// Contains the document ID and the list of changed fields.
    /// </summary>
    public class UpdatedDocument
    {
        public string DocumentId { get; set; } = string.Empty;
        public List<FieldChange> UpdatedFields { get; set; } = new();
    }

    /// <summary>
    /// Event received from blocks-uds-net when data is inserted, updated, or deleted.
    /// Mirror of DataGateway.DomainService.Models.Events.DataChangeEvent.
    /// </summary>
    public class DataChangeEvent
    {
        public required string ProjectKey { get; set; }
        public required string CollectionName { get; set; }
        public required string SchemaName { get; set; }
        public required DataChangeOperation Operation { get; set; }
        /// <summary>
        /// Document data list for Insert/Delete operations (each doc contains _id).
        /// For Insert: the inserted documents. For Delete: the deleted documents.
        /// </summary>
        public List<Dictionary<string, object?>>? Data { get; set; }
        /// <summary>
        /// Updated documents with their changed fields for Update operations.
        /// Each entry contains a DocumentId and the list of field changes.
        /// </summary>
        public List<UpdatedDocument>? UpdatedDocuments { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}

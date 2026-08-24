namespace DomainService.Workflow.Nodes.ActionDataV1
{
    /// <summary>
    /// Parameters for the Data Action node.
    /// All operations (Get/Insert/Update/Delete) use HTTP calls to UDS GraphQL gateway.
    /// </summary>
    public class ActionDataV1Parameters
    {

        public bool RawQueryMode { get; set; } = false;

        public string RawQuery { get; set; } = string.Empty;
        /// <summary>
        /// The collection/schema name to operate on (e.g., "Tasks")
        /// </summary>
        public string CollectionName { get; set; } = string.Empty;

        /// <summary>
        /// The schema display name
        /// </summary>
        public string SchemaName { get; set; } = string.Empty;

        /// <summary>
        /// The project/tenant key
        /// </summary>
        public string ProjectKey { get; set; } = string.Empty;

        /// <summary>
        /// The project short key (slug) for UDS API calls
        /// </summary>
        public string ProjectShortKey { get; set; } = string.Empty;

        /// <summary>
        /// Authentication type: "clientCredential" or "triggerNodeCookie"
        /// </summary>
        public string AuthenticationType { get; set; } = string.Empty;

        /// <summary>
        /// Client ID for authentication (selected from IDP client credentials dropdown)
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// Client Secret for authentication (selected from IDP client credentials dropdown)
        /// </summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// The action type: "getData", "insertData", "updateData", "deleteData"
        /// </summary>
        public string ActionType { get; set; } = string.Empty;

        /// <summary>
        /// Filter criteria for Get/Update/Delete operations (JSON string)
        /// </summary>
        public Dictionary<string, string> Filter { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Field mapping for Insert/Update operations.
        /// Keys are field names, values are expressions or literal values.
        /// </summary>
        public Dictionary<string, string> FieldMapping { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Fields to fetch for getData operations.
        /// Keys are field names, values are field types (for FE display).
        /// When empty, only _id is fetched.
        /// </summary>
        public List<string>? GetFields { get; set; }

        /// <summary>
        /// Base URL for UDS API (injected via transform)
        /// </summary>
        public string ApiBaseUrl { get; set; } = string.Empty;

        public List<SchemaField>? SchemaFields { get; set; } = new List<SchemaField>();
    }

    public class SchemaField
    {
        public string Description { get; set; } = "";
        public bool IsArray { get; set; } = false;
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public List<SchemaField>? Fields { get; set; }
    }


    public class ActionDataGraphqlResponse
    {
        public bool Acknowledged { get; set; }
        public int TotalImpactedData { get; set; }
        public string? ItemId { get; set; }
        public string? Message { get; set; }
    }
}

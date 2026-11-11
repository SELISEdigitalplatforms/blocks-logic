using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Workflow.DomainService.Nodes.ActionIamUserV2
{
    /// <summary>
    /// How the IAM User node gets its bearer token. Saved as camelCase text by the editor; the server also
    /// reads the member's index. The indexes are a contract: members are never renumbered or reordered, and
    /// a new member is only appended with the next free number.
    /// </summary>
    // (namingStrategyType, namingStrategyParameters, allowIntegerValues): integer values are allowed on
    // purpose and written out so nobody switches them off without noticing.
    [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy), new object[0], true)]
    public enum IamUserAuthenticationType
    {
        ClientCredential = 0,
        BlocksAuthentication = 1,
    }

    /// <summary>
    /// The IAM User node's operation. Saved as camelCase text by the editor; the server also reads the
    /// member's index. The indexes are a contract: members are never renumbered or reordered, and a new
    /// member is only appended with the next free number.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy), new object[0], true)]
    public enum IamUserOperation
    {
        GetUsers = 0,
        GetUser = 1,
        CreateUser = 2,
        UpdateUser = 3,
        ActivateUser = 4,
        DeactivateUser = 5,
        AssignRoles = 6,
        RevokeAccess = 7,
    }

    /// <summary>
    /// Saved parameters of the IAM User node (v2). Text keys are expressions resolved per item. The list,
    /// switch and number keys are <see cref="JToken"/>: a JSON value is a literal, a JSON string is an
    /// expression resolved per item to that type; <c>null</c>, a blank string or a missing key means the
    /// default. IAM's address is <see cref="ApiBaseUrl"/>, falling back to the server setting <c>IamBaseUrl</c>.
    /// </summary>
    public class ActionIamUserV2Parameters
    {
        /// <summary>
        /// Base URL of blocks-iam, injected by the editor's transform from <c>BLOCKS_IAM_BASE_URL</c> (as the
        /// Data Action node does with <c>BLOCKS_DATA_BASE_URL</c>). Blank: the server setting <c>IamBaseUrl</c>.
        /// </summary>
        public string ApiBaseUrl { get; set; } = string.Empty;

        public IamUserAuthenticationType AuthenticationType { get; set; } = IamUserAuthenticationType.ClientCredential;

        /// <summary>Client Credential only: the credential's id. The secret is never stored on the node.</summary>
        public string ClientId { get; set; } = string.Empty;

        public IamUserOperation Operation { get; set; } = IamUserOperation.GetUsers;

        /// <summary>Blank: the token's organization (Create: checked against the multi-org setting).</summary>
        public string OrganizationId { get; set; } = string.Empty;

        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;

        /// <summary>List of role slugs; default empty.</summary>
        public JToken? Roles { get; set; }

        /// <summary>Switch; default true. Sent as <c>sendMail</c>.</summary>
        public JToken? SendActivationMail { get; set; }

        /// <summary>Switch; default true. Create, Assign Roles and Revoke Access.</summary>
        public JToken? NotifyUser { get; set; }

        public string Reason { get; set; } = string.Empty;
        public string SearchName { get; set; } = string.Empty;
        public string SearchEmail { get; set; } = string.Empty;

        /// <summary>List of IAM account states; default empty.</summary>
        public JToken? AccountStates { get; set; }

        /// <summary>List of role slugs to filter Get Users by; default empty.</summary>
        public JToken? FilterRoles { get; set; }

        /// <summary>Whole number, 0 or more; default 0 (zero-based).</summary>
        public JToken? Page { get; set; }

        /// <summary>Whole number, 1 to 100; default 10.</summary>
        public JToken? PageSize { get; set; }

        /// <summary><c>add</c> or <c>replace</c> after resolving; Assign Roles only.</summary>
        public string RoleMode { get; set; } = "add";
    }
}

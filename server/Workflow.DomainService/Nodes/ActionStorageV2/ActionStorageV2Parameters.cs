namespace Workflow.DomainService.Nodes.ActionStorageV2
{
    /// <summary>Parameters of the Storage node (SPEC11 § Data contracts).</summary>
    public class ActionStorageV2Parameters
    {
        /// <summary><c>get</c>, <c>delete</c> or <c>rename</c>.</summary>
        public string Operation { get; set; } = ActionStorageV2Node.OperationGet;

        /// <summary><c>blocksAuthentication</c> (the run's delegated token) or <c>clientCredential</c>.</summary>
        public string AuthenticationType { get; set; } = ActionStorageV2Node.AuthBlocks;

        /// <summary>Client credential id when <see cref="AuthenticationType"/> is <c>clientCredential</c>.</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>The file to act on; an expression resolved per item.</summary>
        public string FileId { get; set; } = string.Empty;

        /// <summary>Rename only: the new name; an expression resolved per item.</summary>
        public string NewName { get; set; } = string.Empty;

        /// <summary>Delete only: true deletes permanently, false moves the file to Storage's trash (SPEC11 D5).</summary>
        public bool Permanent { get; set; }
    }
}

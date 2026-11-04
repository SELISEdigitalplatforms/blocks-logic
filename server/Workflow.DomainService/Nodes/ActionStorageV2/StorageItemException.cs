namespace Workflow.DomainService.Nodes.ActionStorageV2
{
    /// <summary>
    /// A Storage node item-level failure whose message is safe to show (SPEC11 § Error mapping). The node turns it
    /// into an error item; it never leaves the node.
    /// </summary>
    public sealed class StorageItemException : Exception
    {
        public StorageItemException()
        {
        }

        public StorageItemException(string message) : base(message)
        {
        }

        public StorageItemException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}

namespace Workflow.DomainService.Nodes
{
    /// <summary>
    /// A node set to "Blocks Authentication" could not get a delegated Blocks token for this run.
    /// The item fails with this message. The node never falls back to the caller's raw
    /// <c>BlocksContext.OAuthToken</c> (that sent the editor's or webhook caller's own token to an
    /// author-chosen URL) and never sends the request without an Authorization header.
    /// A run has a delegated token only when its trigger had an authenticated caller (an authenticated
    /// webhook, a data change written by a user, Execute step / Resume in the editor); schedule, email
    /// and anonymous webhook runs have none — use Client Credential there.
    /// </summary>
    public sealed class NoDelegatedTokenException : InvalidOperationException
    {
        public const string DefaultMessage = "No delegated Blocks token for this run — the trigger has no caller";

        public NoDelegatedTokenException() : base(DefaultMessage) { }
    }
}

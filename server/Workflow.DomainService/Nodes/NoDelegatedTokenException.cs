using Blocks.Genesis;

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
        /// <summary>The run holds no grant: nobody signed in started it.</summary>
        public const string DefaultMessage =
            "No signed-in user started this run (schedule, email or anonymous webhook trigger). " +
            "Blocks Authentication acts as that user, so it cannot work here: set this node to Client Credential.";

        /// <summary>The run holds a grant, but IAM did not turn it into a token.</summary>
        /// <remarks>General on purpose: the node only learns that no token came back, never IAM's reason.</remarks>
        public const string RefusedMessage = "Blocks could not get a token for the user who started this run.";

        public NoDelegatedTokenException() : base(DefaultMessage) { }

        private NoDelegatedTokenException(string message) : base(message) { }

        /// <summary>
        /// Tells the two cases apart, so the author is not sent to Client Credential when the real cause
        /// is that the starting user's token was refused.
        /// </summary>
        public static NoDelegatedTokenException ForCurrentRun()
            => string.IsNullOrWhiteSpace(DelegatedTokenContext.Current)
                ? new NoDelegatedTokenException()
                : new NoDelegatedTokenException(RefusedMessage);
    }
}

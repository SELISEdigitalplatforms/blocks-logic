namespace Blocks.FunctionRunner.Delegation
{
    /// <summary>
    /// Redeems a run's delegation grant for a fresh Blocks access token carrying the caller's
    /// identity — what the function sees as <c>ctx.blocks.accessToken</c>.
    /// <para>
    /// Unlike a secret, a token is optional: the function is told "no token" (<c>undefined</c>)
    /// rather than failing. So the contract is a single nullable answer, and an implementation
    /// never throws for a grant that cannot be redeemed — expired, revoked, a deactivated user,
    /// IAM unreachable or slow. Only cancellation of the run itself propagates.
    /// </para>
    /// <para>
    /// The token is returned to the caller and nowhere else: an implementation never logs it,
    /// nor the grant id, which is a bearer credential too.
    /// </para>
    /// </summary>
    public interface IRunAccessTokenResolver
    {
        /// <returns>The access token, or <c>null</c> when none could be obtained.</returns>
        Task<string?> RedeemAsync(string tenantId, string grantId, CancellationToken cancellationToken);
    }
}

namespace Common.InternalService.Access
{
    /// <summary>
    /// How the <see cref="EndpointAccessPolicy.Roles"/> and <see cref="EndpointAccessPolicy.Permissions"/>
    /// rules combine when both are configured. A rule with no values is not configured and never
    /// participates, so the combinator only matters once both lists carry at least one entry.
    /// </summary>
    public enum EndpointAccessCombine
    {
        /// <summary>The caller passes if either configured rule passes.</summary>
        Or = 0,

        /// <summary>The caller must pass every configured rule.</summary>
        And = 1,
    }
}

using Blocks.Genesis;

namespace Functions.DomainService.Utils
{
    /// <summary>
    /// The ambient tenant, wrapped behind an interface rather than read directly from
    /// <c>BlocksContext.GetContext()</c> everywhere it is needed.
    /// <para>
    /// This exists for one reason: a validator that calls the static <c>BlocksContext</c>
    /// directly cannot be unit-tested without also standing up a real ambient context: this
    /// abstraction lets a test substitute a fixed tenant id instead. Application code with an
    /// HTTP request in flight should still prefer reading <c>BlocksContext.GetContext()</c>
    /// directly where that is already the norm (controllers, most services); this is
    /// specifically for the few places — validators — where a plain constructor-injected
    /// dependency is the only thing that is easy to fake.
    /// </para>
    /// </summary>
    public interface ITenantAccessor
    {
        /// <summary>The ambient tenant id, or empty when no context is set.</summary>
        string TenantId { get; }
    }

    public class TenantAccessor : ITenantAccessor
    {
        public string TenantId => BlocksContext.GetContext()?.TenantId ?? string.Empty;
    }
}

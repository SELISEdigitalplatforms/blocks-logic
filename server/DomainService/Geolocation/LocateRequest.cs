using Blocks.Genesis;

namespace DomainService.Geolocation
{
    public class LocateRequest
    {
        /// <summary>
        /// Use custom ip lookup provider.
        /// </summary>
        public bool UseCustomProvider { get; set; } = false;
        
        /// <summary>
        /// Project key for tenant context.
        /// </summary>
    }
}
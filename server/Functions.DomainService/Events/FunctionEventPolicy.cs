using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Events
{
    /// <summary>Which event types a run may send: a fixed rule by trigger, no per-function list.</summary>
    public static class FunctionEventPolicy
    {
        /// <summary>
        /// A function whose trigger is Public (anyone with the public key can call it) may send no events; any other
        /// function may send every catalog type that is not Public-only (today: <c>FunctionEvent</c>).
        /// </summary>
        public static List<string> AllowedTypes(AuthMode authMode) =>
            [.. FunctionEventCatalog.All
                .Where(definition => authMode != AuthMode.Public || definition.PublicAllowed)
                .Select(definition => definition.Type)];
    }
}

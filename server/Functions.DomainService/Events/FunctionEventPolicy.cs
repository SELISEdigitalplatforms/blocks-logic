using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Events
{
    /// <summary>Which event types a run may send: its version's list, narrowed for a Public trigger.</summary>
    public static class FunctionEventPolicy
    {
        /// <summary>
        /// The types in <paramref name="settings"/> that the catalog knows, in the saved order. A function whose
        /// trigger is Public (anyone with the public key can call it) may send only the types marked
        /// <c>PublicAllowed</c>. No settings means no events.
        /// </summary>
        public static List<string> AllowedTypes(FunctionEventSettingsEntity? settings, AuthMode authMode)
        {
            var allowed = new List<string>();
            foreach (var setting in settings?.Events ?? [])
            {
                var definition = FunctionEventCatalog.Find(setting.Type);
                if (definition is null || allowed.Contains(definition.Type)) continue;
                if (authMode == AuthMode.Public && !definition.PublicAllowed) continue;
                allowed.Add(definition.Type);
            }
            return allowed;
        }
    }
}

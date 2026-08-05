using System.Collections.Immutable;

namespace Common.InternalService.Storage
{
    public enum StorageStrategyCategory
    {
        Cloud,
        Local
    }

    public static class StorageTypes
    {
        private static readonly ImmutableDictionary<string, StorageStrategyCategory> TypeToCategory =
            ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new[]
            {
                   new KeyValuePair<string, StorageStrategyCategory>("azure", StorageStrategyCategory.Cloud),
                   new KeyValuePair<string, StorageStrategyCategory>("aws", StorageStrategyCategory.Cloud),
                   new KeyValuePair<string, StorageStrategyCategory>("sftpstorage", StorageStrategyCategory.Local)
            });

        public static bool TryGetCategory(string type, out StorageStrategyCategory category)
        {
            return TypeToCategory.TryGetValue(type.ToLower(), out category);
        }
    }
}

using Blocks.Genesis;

namespace XUnitTest.TestHelpers
{
    // Shared helper to install a BlocksContext for tests that read the ambient
    // tenant/user via BlocksContext.GetContext(). Uses reflection so it stays
    // compatible as Genesis grows the Create parameter list.
    internal static class TestBlocksContext
    {
        public static void Set(string tenantId = "tenant-123", string userId = "user-123", string orgId = "org-123")
        {
            var context = Create(tenantId, userId, orgId);
            if (context is null) return;
            BlocksContext.SetContext(context, true);
        }

        /// <summary>An authenticated context without installing it, e.g. a webhook caller handed to a service.</summary>
        public static BlocksContext? Create(string tenantId = "tenant-123", string userId = "user-123", string orgId = "org-123")
        {
            var createMethods = typeof(BlocksContext)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.Name == "Create" && m.ReturnType == typeof(BlocksContext))
                .ToList();

            var create = createMethods.FirstOrDefault(m => m.GetParameters().Length >= 15)
                         ?? createMethods.FirstOrDefault(m => m.GetParameters().Length == 14);

            return create?.CreateContext(new object[]
            {
                tenantId, Array.Empty<string>(), userId, true, string.Empty, orgId,
                DateTime.UtcNow.AddHours(1), "test@example.com", Array.Empty<string>(),
                "testuser", string.Empty, "Test User", string.Empty, tenantId, string.Empty
            });
        }

        /// <summary>An impersonated context: <c>TenantId</c> is the target tenant, <c>OriginalTenantId</c> the caller's own.</summary>
        public static void SetImpersonated(string tenantId = "tenant-123", string originalTenantId = "root", string userId = "user-123")
        {
            var ctor = typeof(BlocksContext)
                .GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Single(c => c.GetParameters().Length >= 16);
            var args = new object?[]
            {
                tenantId, Array.Empty<string>(), userId, true, string.Empty, "org-123",
                DateTime.UtcNow.AddHours(1), "test@example.com", Array.Empty<string>(),
                "testuser", string.Empty, "Test User", string.Empty, originalTenantId, string.Empty,
                true, "session", string.Empty, string.Empty
            };
            BlocksContext.SetContext((BlocksContext)ctor.Invoke(args.Take(ctor.GetParameters().Length).ToArray()), true);
        }

        public static void Clear() => BlocksContext.ClearContext();
    }
}

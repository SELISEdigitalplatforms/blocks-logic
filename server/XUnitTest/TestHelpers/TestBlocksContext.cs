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
            var createMethods = typeof(BlocksContext)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.Name == "Create" && m.ReturnType == typeof(BlocksContext))
                .ToList();

            var create = createMethods.FirstOrDefault(m => m.GetParameters().Length >= 15)
                         ?? createMethods.FirstOrDefault(m => m.GetParameters().Length == 14);

            if (create == null) return;

            var context = create.CreateContext(new object[]
            {
                tenantId, Array.Empty<string>(), userId, true, string.Empty, orgId,
                DateTime.UtcNow.AddHours(1), "test@example.com", Array.Empty<string>(),
                "testuser", string.Empty, "Test User", string.Empty, tenantId, string.Empty
            });
            BlocksContext.SetContext(context, true);
        }

        public static void Clear() => BlocksContext.ClearContext();
    }
}

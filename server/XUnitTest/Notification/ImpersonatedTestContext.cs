using System.Reflection;
using Blocks.Genesis;

namespace XUnitTest.Notification
{
    // Installs a BlocksContext that is flagged as impersonated, which is the branch the
    // notification repositories use to reach the root database instead of the tenant one.
    // Reflection keeps this compatible as Genesis grows the Create parameter list, exactly as
    // XUnitTest.TestHelpers.TestBlocksContext does for the ordinary case.
    internal static class ImpersonatedTestContext
    {
        public static bool Set(string tenantId = "tenant-123", string userId = "user-123")
        {
            var create = typeof(BlocksContext)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Create" && m.ReturnType == typeof(BlocksContext))
                .FirstOrDefault(m => m.GetParameters().Length >= 16
                                     && m.GetParameters()[15].ParameterType == typeof(bool));

            if (create == null) return false;

            var context = create.CreateContext(new object[]
            {
                tenantId, Array.Empty<string>(), userId, true, string.Empty, "org-123",
                DateTime.UtcNow.AddHours(1), "test@example.com", Array.Empty<string>(),
                "testuser", string.Empty, "Test User", string.Empty, tenantId, string.Empty, true
            });
            BlocksContext.SetContext(context, true);

            return BlocksContext.GetContext()?.Impersonated == true;
        }
    }
}

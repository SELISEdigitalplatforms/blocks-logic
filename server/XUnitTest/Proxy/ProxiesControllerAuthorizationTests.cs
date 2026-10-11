using System.Reflection;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Utilities.Api.Controllers;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Every control-plane action needs a proxy permission, not just a signed-in user (Deep Scan P-1).
    /// Only the data-plane <see cref="ProxiesController.Gateway"/> stays anonymous: it enforces each
    /// proxy's own "Who can call it" policy itself.
    /// </summary>
    public class ProxiesControllerAuthorizationTests
    {
        private const string Read = "blocks-logic::proxy::read";
        private const string Manage = "blocks-logic::proxy::manage";

        private static IEnumerable<MethodInfo> AllActions() =>
            typeof(ProxiesController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName);

        private static string? ResourceOf(MethodInfo action) =>
            action.GetCustomAttribute<ProtectedEndPointAttribute>()?.ResourceName;

        [Theory]
        [InlineData(nameof(ProxiesController.List), Read)]
        [InlineData(nameof(ProxiesController.Get), Read)]
        [InlineData(nameof(ProxiesController.ListVersions), Read)]
        [InlineData(nameof(ProxiesController.ListExecutions), Read)]
        [InlineData(nameof(ProxiesController.GetExecution), Read)]
        [InlineData(nameof(ProxiesController.GetOverview), Read)]
        [InlineData(nameof(ProxiesController.Create), Manage)]
        [InlineData(nameof(ProxiesController.Update), Manage)]
        [InlineData(nameof(ProxiesController.SetEnabled), Manage)]
        [InlineData(nameof(ProxiesController.Delete), Manage)]
        [InlineData(nameof(ProxiesController.Revert), Manage)]
        [InlineData(nameof(ProxiesController.Test), Manage)]
        [InlineData(nameof(ProxiesController.PreviewOpenApi), Manage)]
        public void ControlPlaneActionNeedsItsPermission(string actionName, string expected)
        {
            var action = typeof(ProxiesController).GetMethod(actionName)!;
            ResourceOf(action).Should().Be(expected);
        }

        [Fact]
        public void EveryActionExceptGatewayIsProtected()
        {
            AllActions()
                .Where(m => m.Name != nameof(ProxiesController.Gateway))
                .Where(m => ResourceOf(m) is null)
                .Select(m => m.Name)
                .Should().BeEmpty("a bare [Authorize] lets any signed-in user manage proxies and read their logs");
        }

        [Fact]
        public void GatewayStaysAnonymousAndControllerHasNoClassLevelAuth()
        {
            var gateway = typeof(ProxiesController).GetMethod(nameof(ProxiesController.Gateway))!;
            gateway.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
            ResourceOf(gateway).Should().BeNull();

            // A class-level [Authorize] would close the public gateway.
            typeof(ProxiesController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).Should().BeEmpty();
        }
    }
}

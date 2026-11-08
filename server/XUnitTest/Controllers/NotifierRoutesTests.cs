using System.Reflection;
using Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;

namespace XUnitTest.Controllers
{
    /// <summary>
    /// The send actions moved from <see cref="NotifierController"/> to <see cref="NotifierSendController"/> (SonarQube
    /// S6960: two responsibilities that share no dependency). Every action must still answer on
    /// <c>/api/Notifier/{action}</c>, which internal services and the notification bell call. Routes are composed with
    /// MVC's own <see cref="AttributeRouteModel"/> helpers, after the <c>api</c> prefix GlobalApiRoutePrefixConvention adds.
    /// </summary>
    public class NotifierRoutesTests
    {
        private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
            new[] { typeof(NotifierController), typeof(NotifierSendController) }
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName)
                    .Select(m => (t, m)));

        private static string RouteOf(Type controller, MethodInfo action)
        {
            var classTemplate = controller.GetCustomAttribute<RouteAttribute>()!.Template;
            var combined = AttributeRouteModel.CombineTemplates("api", classTemplate);
            return AttributeRouteModel.ReplaceTokens(combined!, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["controller"] = controller.Name[..^"Controller".Length],
                ["action"] = action.Name,
            })!;
        }

        [Fact]
        public void EveryNotifierAction_KeepsItsApiNotifierRoute()
        {
            var routes = Actions().ToDictionary(a => a.Action.Name, a => RouteOf(a.Controller, a.Action));

            routes.Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["Notify"] = "api/Notifier/Notify",
                ["SendSecretNotification"] = "api/Notifier/SendSecretNotification",
                ["GetUnreadNotificationsBySubscriptionFilter"] = "api/Notifier/GetUnreadNotificationsBySubscriptionFilter",
                ["MarkAllNotificationAsRead"] = "api/Notifier/MarkAllNotificationAsRead",
                ["MarkNotificationAsRead"] = "api/Notifier/MarkNotificationAsRead",
                ["GetNotifications"] = "api/Notifier/GetNotifications",
            });
        }

        [Fact]
        public void NoActionTemplateOverridesTheControllerRoute()
        {
            Actions().Where(a => a.Action.GetCustomAttributes().OfType<IRouteTemplateProvider>().Any(p => p.Template != null))
                .Should().BeEmpty("each action's route is the controller's [Route] with its own name");
        }

        [Fact]
        public void BothControllers_AreApiControllers()
        {
            typeof(NotifierController).GetCustomAttribute<ApiControllerAttribute>().Should().NotBeNull();
            typeof(NotifierSendController).GetCustomAttribute<ApiControllerAttribute>().Should().NotBeNull();
        }
    }
}

using System.Reflection;
using BlocksTemplate.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;

namespace XUnitTest.Functions
{
    /// <summary>
    /// <see cref="FunctionsController"/> carries two surfaces that used to be two controllers: the
    /// permission-gated management actions on <c>/api/functions/{action}</c>, and the public
    /// invocation endpoints on <c>/api/fn/…</c>. Merging them put both under one class-level
    /// <c>[Route]</c> and one set of attributes, so the things that kept them apart — absolute
    /// route templates, per-action authorization — are now invariants worth pinning rather than
    /// facts guaranteed by the file layout.
    /// <para>
    /// Routes are composed here with MVC's own <see cref="AttributeRouteModel"/> helpers, the same
    /// ones attribute routing uses, rather than by re-implementing the combination rules.
    /// </para>
    /// </summary>
    public class FunctionsControllerRoutingTests
    {
        /// <summary>What the controller's own <c>[Route]</c> says.</summary>
        private const string ControllerTemplate = "[controller]/[action]";

        /// <summary>
        /// The same template after <c>GlobalApiRoutePrefixConvention("api")</c> has run. The
        /// convention only ever rewrites controller-level selectors, which is precisely why the
        /// public actions have to spell out their own <c>api</c> segment.
        /// </summary>
        private static readonly string PrefixedControllerTemplate =
            AttributeRouteModel.CombineTemplates("api", ControllerTemplate)!;

        private static MethodInfo Action(string name) =>
            typeof(FunctionsController).GetMethod(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"{name} is no longer an action on FunctionsController");

        /// <summary>Every action MVC would route: public, instance, declared on the controller.</summary>
        private static IEnumerable<MethodInfo> AllActions() =>
            typeof(FunctionsController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName);

        private static string RouteOf(MethodInfo action, string controllerTemplate)
        {
            var template = action.GetCustomAttributes()
                .OfType<IRouteTemplateProvider>()
                .Select(a => a.Template)
                .FirstOrDefault();

            var combined = AttributeRouteModel.CombineTemplates(controllerTemplate, template);
            return AttributeRouteModel.ReplaceTokens(
                combined ?? string.Empty,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["controller"] = "Functions",
                    ["action"] = action.Name,
                })!;
        }

        private static bool HasProtectedEndPoint(MemberInfo member) =>
            member.GetCustomAttributes().Any(a => a.GetType().Name == "ProtectedEndPointAttribute");

        [Theory]
        [InlineData(nameof(FunctionsController.Invoke), "api/fn/{functionId}/{**path}")]
        [InlineData(nameof(FunctionsController.PollRun), "api/fn/runs/{runId}")]
        public void PublicEndpointsKeepTheirOwnPathWhateverTheControllerTemplateIs(string actionName, string expected)
        {
            var action = Action(actionName);

            // The absolute (`~/`) template has to survive both the bare controller route and the
            // prefixed one: if it ever loses its leading `~/`, the endpoint silently moves to
            // /api/functions/invoke/... and every tenant's published URL 404s.
            RouteOf(action, ControllerTemplate).Should().Be(expected);
            RouteOf(action, PrefixedControllerTemplate).Should().Be(expected);
        }

        [Fact]
        public void InvokeIsAGatewayStyleCatchAllOnEveryMethod()
        {
            // The proxy gateway's shape, deliberately: the two methods a trigger can pick from,
            // anything after the id is the handler's input.path, and a body cap enforced before
            // Kestrel's generic 413. The trigger's own method is enforced inside the service.
            var verbs = Action(nameof(FunctionsController.Invoke)).GetCustomAttribute<AcceptVerbsAttribute>();
            verbs.Should().NotBeNull();
            verbs!.HttpMethods.Should().BeEquivalentTo(["GET", "POST"]);
            verbs.Route.Should().Be("~/api/fn/{functionId}/{**path}");

            Action(nameof(FunctionsController.Invoke)).GetCustomAttribute<RequestSizeLimitAttribute>().Should().NotBeNull();
        }

        [Fact]
        public void PollRunDoesNotSitUnderTheCatchAll()
        {
            // `runs/{runId}` would otherwise match `{functionId}/{**path}` with functionId = "runs".
            // ASP.NET Core prefers the literal segment, but that is a routing subtlety worth pinning
            // rather than trusting: the two templates must stay distinguishable by more than luck.
            var poll = Action(nameof(FunctionsController.PollRun)).GetCustomAttribute<HttpGetAttribute>();
            poll!.Template.Should().StartWith("~/api/fn/runs/");
        }

        [Theory]
        [InlineData(nameof(FunctionsController.Create), "api/Functions/Create")]
        [InlineData(nameof(FunctionsController.GetRun), "api/Functions/GetRun")]
        [InlineData(nameof(FunctionsController.CancelRun), "api/Functions/CancelRun")]
        public void ManagementEndpointsStayOnTheConventionPath(string actionName, string expected)
            => RouteOf(Action(actionName), PrefixedControllerTemplate).Should().Be(expected);

        [Fact]
        public void InvokeIsAnonymous()
        {
            // A tenant key identifies a tenant but authenticates no user; whether the caller may
            // proceed is the function's own trigger config, decided in the invocation service.
            Action(nameof(FunctionsController.Invoke))
                .GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();

            HasProtectedEndPoint(Action(nameof(FunctionsController.Invoke))).Should().BeFalse();
        }

        [Fact]
        public void TheControllerItselfGatesNothing()
        {
            // A class-level [Authorize] or [ProtectedEndPoint] here would shut off public
            // invocation for every tenant, and would surface as callers' 401s rather than as
            // anything failing on this side. Authorization is per-action, on purpose.
            typeof(FunctionsController).GetCustomAttribute<AuthorizeAttribute>().Should().BeNull();
            HasProtectedEndPoint(typeof(FunctionsController)).Should().BeFalse();
        }

        [Fact]
        public void EveryManagementActionIsPermissionGated()
        {
            var publicSurface = new[] { nameof(FunctionsController.Invoke), nameof(FunctionsController.PollRun) };

            var ungated = AllActions()
                .Where(m => !publicSurface.Contains(m.Name))
                .Where(m => !HasProtectedEndPoint(m))
                .Select(m => m.Name)
                .ToList();

            // The merge removed the type boundary that used to make this obvious. An action added
            // to this file inherits no authorization from anywhere, so it has to declare its own.
            ungated.Should().BeEmpty(
                "every action outside the public /api/fn surface must declare its own [ProtectedEndPoint]");
        }

        [Fact]
        public void PollRunRequiresASignedInCaller()
            => Action(nameof(FunctionsController.PollRun))
                .GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
    }
}

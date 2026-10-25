using System.Reflection;
using System.Security.Claims;
using Blocks.Genesis;
using BlocksTemplate.Api.Security;
using DomainService.Notification;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Security
{
    /// <summary>
    /// The token path of the notifier send actions: no token, a token that failed validation, a valid token
    /// without / with <c>blocks-logic::notifier::send</c>, and a failing lookup (refused, never a 500).
    /// </summary>
    public class NotifierSendPermissionTests
    {
        private const string Bearer = "Bearer some.jwt.value";

        private readonly Mock<IAuthorizationService> _authorization = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly NotifierSendPermission _sut;

        public NotifierSendPermissionTests()
        {
            _sut = new NotifierSendPermission(_authorization.Object, _tenants.Object,
                NullLogger<NotifierSendPermission>.Instance);
        }

        private static DefaultHttpContext Anonymous(string? authorization = null)
        {
            var context = new DefaultHttpContext();
            if (authorization is not null)
                context.Request.Headers.Authorization = authorization;
            return context;
        }

        private static DefaultHttpContext SignedIn() => new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1")], "Bearer")),
        };

        private void AuthorizationReturns(AuthorizationResult result) =>
            _authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(),
                NotifierSendPermission.ProtectedPolicy)).ReturnsAsync(result);

        [Fact]
        public async Task NoToken_IsNone()
        {
            (await _sut.CheckAsync(Anonymous())).Should().Be(NotifierCallerToken.None);

            _authorization.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ATokenThatFailedValidation_IsInvalid()
        {
            (await _sut.CheckAsync(Anonymous(Bearer))).Should().Be(NotifierCallerToken.Invalid);

            _authorization.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task AValidTokenWithThePermission_IsPermitted()
        {
            var context = SignedIn();
            object? resourceSeen = null;
            _authorization.Setup(a => a.AuthorizeAsync(context.User, context, NotifierSendPermission.ProtectedPolicy))
                .Callback(() => resourceSeen = context.Items[NotifierSendPermission.ResourceItemKey])
                .ReturnsAsync(AuthorizationResult.Success());

            (await _sut.CheckAsync(context)).Should().Be(NotifierCallerToken.Permitted);

            resourceSeen.Should().Be("blocks-logic::notifier::send");
            context.Items.Should().NotContainKey(NotifierSendPermission.ResourceItemKey);
        }

        [Fact]
        public async Task AValidTokenWithoutThePermission_IsNoPermission()
        {
            AuthorizationReturns(AuthorizationResult.Failed());

            (await _sut.CheckAsync(SignedIn())).Should().Be(NotifierCallerToken.NoPermission);
        }

        [Fact]
        public async Task AFailingPermissionLookup_IsNoPermissionNotAnError()
        {
            var context = SignedIn();
            _authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(),
                NotifierSendPermission.ProtectedPolicy)).ThrowsAsync(new InvalidOperationException("db down"));

            (await _sut.CheckAsync(context)).Should().Be(NotifierCallerToken.NoPermission);

            context.Items.Should().NotContainKey(NotifierSendPermission.ResourceItemKey);
        }

        [Fact]
        public async Task ACancelledRequestIsNotHidden()
        {
            _authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(),
                NotifierSendPermission.ProtectedPolicy)).ThrowsAsync(new OperationCanceledException());

            var act = () => _sut.CheckAsync(SignedIn());

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task ANullContextIsRejected()
        {
            var act = () => _sut.CheckAsync(null!);

            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public void ResourceItemKey_IsTheKeyGenesisReads()
        {
            // Genesis reads the resource of a call without [ProtectedEndPoint] from this Items key (internal const).
            var field = typeof(ProtectedEndPointAttribute).Assembly
                .GetType("Blocks.Genesis.BlocksConstants")!
                .GetField("ProtectedResourceName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

            field.GetValue(null).Should().Be(NotifierSendPermission.ResourceItemKey);
        }

        [Fact]
        public void ProtectedPolicy_IsThePolicyOfProtectedEndPoint()
        {
            new ProtectedEndPointAttribute("x").Policy.Should().Be(NotifierSendPermission.ProtectedPolicy);
        }

        [Fact]
        public async Task OptionalTokenPolicy_LetsACallerWithoutATokenThrough()
        {
            var services = new ServiceCollection().AddLogging();
            services.AddSingleton(_tenants.Object);
            services.AddNotifierSendPolicy();
            await using var provider = services.BuildServiceProvider();
            var authorization = provider.GetRequiredService<IAuthorizationService>();

            var result = await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()),
                NotifierOptionalTokenAttribute.PolicyName);

            result.Succeeded.Should().BeTrue();
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<INotifierSendPermission>().Should().BeOfType<NotifierSendPermission>();
        }

        [Fact]
        public void OptionalTokenAttribute_UsesOnlyItsPolicy()
        {
            var attribute = new NotifierOptionalTokenAttribute();

            attribute.Policy.Should().Be(NotifierOptionalTokenAttribute.PolicyName);
            attribute.Roles.Should().BeNull();
            attribute.AuthenticationSchemes.Should().BeNull();
        }
    }
}

using System.Reflection;
using Authentication.DomainService.OAuth.SocialServices;
using DomainService.OAuth.SocialServices;
using DomainService.Services;
using DomainService.OAuth;
using DomainService.Shared;
using DomainService.Utilities;
using FluentAssertions;
using FluentValidation;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Mfa.DomainService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace XUnitTest.Composition
{
    /// <summary>
    /// Unit tests for <see cref="ApplicationServiceCollectionExtensions"/>, the composition root
    /// for this service. A registration that is missing, duplicated with a different
    /// implementation, or given the wrong lifetime does not fail the build; it fails at the first
    /// request that resolves it, or worse, it quietly shares state across tenants. These tests
    /// pin the registrations rather than the call.
    /// </summary>
    public class ApplicationServiceCollectionExtensionsTests
    {
        private readonly IServiceCollection _services = new ServiceCollection();

        public ApplicationServiceCollectionExtensionsTests() => _services.RegisterAllServices();

        private ServiceDescriptor? Descriptor<TService>() =>
            _services.LastOrDefault(d => d.ServiceType == typeof(TService));

        [Fact]
        public void Registration_produces_a_populated_container()
        {
            _services.Should().NotBeEmpty();
        }

        [Theory]
        [InlineData(typeof(IAuthenticationDomainService))]
        [InlineData(typeof(IAuthenticationRepository))]
        [InlineData(typeof(IOAuthTokenProvider))]
        [InlineData(typeof(IOAuthJwtAccessTokenManager))]
        [InlineData(typeof(IJwtAccessTokenProvider))]
        [InlineData(typeof(IUserManagementMutationService))]
        [InlineData(typeof(IUserRepository))]
        [InlineData(typeof(IIdentityAccessManagementService))]
        [InlineData(typeof(IMfaManagementService))]
        [InlineData(typeof(IMfaManagementRepository))]
        public void The_service_contracts_the_api_depends_on_are_registered(Type contract)
        {
            _services.Should().Contain(d => d.ServiceType == contract);
        }

        [Fact]
        public void Every_registration_supplies_an_implementation()
        {
            // A descriptor with no implementation type, factory or instance resolves to null at
            // request time rather than throwing here, so it is worth asserting explicitly.
            _services.Should().OnlyContain(d =>
                d.ImplementationType != null ||
                d.ImplementationFactory != null ||
                d.ImplementationInstance != null);
        }

        [Fact]
        public void Every_registered_implementation_actually_implements_its_contract()
        {
            var mismatched = _services
                .Where(d => d.ImplementationType != null)
                .Where(d => !d.ServiceType.IsAssignableFrom(d.ImplementationType!)
                            && !d.ServiceType.IsGenericTypeDefinition)
                .Select(d => $"{d.ServiceType.Name} -> {d.ImplementationType!.Name}")
                .ToList();

            mismatched.Should().BeEmpty();
        }

        [Fact]
        public void Every_registered_implementation_is_constructible()
        {
            // An abstract or interface implementation type compiles but cannot be activated.
            var notConstructible = _services
                .Where(d => d.ImplementationType != null)
                .Where(d => d.ImplementationType!.IsAbstract || d.ImplementationType.IsInterface)
                .Select(d => d.ImplementationType!.Name)
                .ToList();

            notConstructible.Should().BeEmpty();
        }

        [Fact]
        public void Every_registered_implementation_exposes_a_public_constructor()
        {
            var unconstructable = _services
                .Where(d => d.ImplementationType != null)
                .Where(d => d.ImplementationType!.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0)
                .Select(d => d.ImplementationType!.Name)
                .ToList();

            unconstructable.Should().BeEmpty();
        }

        [Fact]
        public void The_social_login_providers_are_all_registered()
        {
            // The provider factory resolves these by concrete type, so a missing one fails only
            // when a user actually signs in with that provider.
            foreach (var provider in new[]
                     {
                         typeof(GoogleLogInService), typeof(MicrosoftLogInService),
                         typeof(BYOSsoLogInService), typeof(GithubLogInService),
                         typeof(LinkedinLogInService), typeof(TwitterLogInService),
                         typeof(AppleLogInService), typeof(FaceBookLogInService)
                     })
            {
                _services.Should().Contain(d => d.ServiceType == provider,
                    $"{provider.Name} is resolved by concrete type");
            }
        }

        [Fact]
        public void The_social_login_provider_lookup_is_registered()
        {
            _services.Should().Contain(d => d.ServiceType == typeof(ISocialLogInServiceProvider));
        }

        [Fact]
        public void The_request_validators_are_registered()
        {
            var validators = _services
                .Where(d => d.ServiceType.IsGenericType
                            && d.ServiceType.GetGenericTypeDefinition() == typeof(IValidator<>))
                .ToList();

            validators.Should().NotBeEmpty();
            validators.Should().OnlyContain(d => d.ImplementationType != null);
        }

        [Fact]
        public void No_contract_is_registered_twice_with_different_implementations()
        {
            // A duplicate wins by last registration, so two different implementations behind one
            // contract means the losing one is silently unreachable.
            var conflicting = _services
                .Where(d => d.ImplementationType != null)
                .GroupBy(d => d.ServiceType)
                .Where(g => g.Select(d => d.ImplementationType).Distinct().Count() > 1)
                .Select(g => g.Key.Name)
                .ToList();

            conflicting.Should().BeEmpty();
        }

        [Fact]
        public void Registering_twice_does_not_change_which_implementation_wins()
        {
            // Startup paths differ between the api and the worker, so the extension has to be
            // safe to apply more than once.
            var before = Descriptor<IAuthenticationDomainService>()!.ImplementationType;

            _services.RegisterAllServices();

            Descriptor<IAuthenticationDomainService>()!.ImplementationType.Should().Be(before);
        }
    }
}

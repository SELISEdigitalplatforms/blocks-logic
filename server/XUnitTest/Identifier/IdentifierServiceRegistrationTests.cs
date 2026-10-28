using DomainService.ManagedService;
using DomainService.ManagedService.Services;
using DomainService.People;
using DomainService.Projects;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace XUnitTest.Identifier
{
    /// <summary>
    /// The Identifier domain's <c>AddApplicationServices</c>, inspected on the descriptors.
    /// Both hosts (Api and Worker) call it, and it can run more than once per host.
    /// </summary>
    public class IdentifierServiceRegistrationTests
    {
        [Theory]
        [InlineData(typeof(IProjectManagementService), typeof(ProjectManagementService))]
        [InlineData(typeof(IProjectRepository), typeof(ProjectRepository))]
        [InlineData(typeof(IPeopleService), typeof(PeopleService))]
        [InlineData(typeof(IPeopleRepository), typeof(PeopleRepository))]
        [InlineData(typeof(IServiceManagement), typeof(ServiceManagement))]
        [InlineData(typeof(IServiceManagementRepository), typeof(ServiceManagementRepository))]
        [InlineData(typeof(IServiceBusConnectionStringResolver), typeof(ServiceBusConnectionStringResolver))]
        public void Registers_each_service_as_a_singleton(Type service, Type implementation)
        {
            var services = new ServiceCollection();

            services.AddApplicationServices();

            var descriptor = services.Last(d => d.ServiceType == service);
            descriptor.ImplementationType.Should().Be(implementation);
            descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        }

        [Fact]
        public void Calling_it_twice_registers_the_service_bus_resolver_once()
        {
            var services = new ServiceCollection();

            services.AddApplicationServices();
            services.AddApplicationServices();

            services.Count(d => d.ServiceType == typeof(IServiceBusConnectionStringResolver)).Should().Be(1);
        }
    }
}

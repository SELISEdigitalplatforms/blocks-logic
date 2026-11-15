using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.DomainService;
using Workflow.DomainService.Services;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Sonar S107: WorkflowExecutionService takes its stores and its trigger collaborators as two groups; the engine
    /// registration must provide both, plus the webhook intake the trigger group carries.
    /// </summary>
    public class WorkflowExecutionDependenciesRegistrationTests
    {
        [Theory]
        [InlineData(typeof(WorkflowExecutionStores))]
        [InlineData(typeof(WorkflowTriggerAccess))]
        [InlineData(typeof(IWebhookIntake))]
        [InlineData(typeof(IWebhookFileStore))]
        public void The_engine_registers_each_dependency_group_once_as_a_singleton(Type serviceType)
        {
            var services = new ServiceCollection();

            services.AddWorkflowExecutionEngine();

            services.Where(d => d.ServiceType == serviceType).Should().ContainSingle()
                .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        }
    }
}

using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.ActionAIAgentV1;
using Workflow.DomainService.Nodes.ActionSendMailV1;
using Workflow.DomainService.Nodes.ActionHttpRequestV1;
using Workflow.DomainService.Nodes.ActionDataV1;
using Microsoft.Extensions.DependencyInjection;
using Blocks.Extension.DependencyInjection;
using Workflow.DomainService.Nodes.TriggerEmailV1;
using Workflow.DomainService.Nodes.TriggerDataV1;
using Workflow.DomainService.Nodes.TriggerScheduleV1;
using Workflow.DomainService.Nodes.LogicIFV1;
using Workflow.DomainService.Nodes.TriggerWebhookV1;
using Workflow.DomainService.Nodes.TransformSetFieldV1;
using Workflow.DomainService.Nodes.TransformCodeV1;
using Workflow.DomainService.Services;
using DomainService.Utilities;

namespace Workflow.DomainService
{
    /// <summary>
    /// Extension methods for registering Workflow Execution Engine services
    /// Add this to your ServiceRegistry.cs or Program.cs
    /// </summary>
    public static class WorkflowExecutionServiceCollectionExtensions
    {
        public static IServiceCollection AddWorkflowExecutionEngine(this IServiceCollection services)
        {
            // register business services
            services.AddSingleton<IWorkflowService, WorkflowService>();
            services.AddSingleton<IWorkflowExecutionService, WorkflowExecutionService>();
            services.AddSingleton<IWorkflowEngineService, WorkflowEngineService>();
            services.AddSingleton<IWorkflowNotificationService, WorkflowNotificationService>();
            services.AddSingleton<IWorkflowVersionService, WorkflowVersionService>();
            services.AddSingleton<IWorkflowAuthService, WorkflowAuthService>();

            // register repositories
            services.AddSingleton<IWorkflowRepository, WorkflowRepository>();
            services.AddSingleton<IWorkflowVersionRepository, WorkflowVersionRepository>();
            services.AddSingleton<IWorkflowExecutionRepository, WorkflowExecutionRepository>();

            //  rigister node executors

            // Trigger nodes
            services.AddSingleton<INodeExecutor, TriggerWebhookV1Node>();
            services.AddSingleton<INodeExecutor, TriggerEmailV1Node>();
            services.AddSingleton<INodeExecutor, TriggerDataV1Node>();
            services.AddSingleton<INodeExecutor, TriggerScheduleV1Node>();

            // Logic nodes
            services.AddSingleton<INodeExecutor, LogicIfV1Node>();


            // Transform nodes
            services.AddSingleton<INodeExecutor, TransformSetFieldV1Node>();
            services.AddSingleton<INodeExecutor, TransformCodeV1Node>();

            // Action nodes
            services.AddSingleton<INodeExecutor, ActionAIAgentV1Node>();
            services.AddSingleton<INodeExecutor, ActionSendMailV1Node>();
            services.AddSingleton<INodeExecutor, ActionHttpRequestV1Node>();
            services.AddSingleton<INodeExecutor, ActionDataV1Node>();


            // end register node executors

            services.AddSingleton<IClientCredentialTokenService, ClientCredentialTokenService>();

            services.RegisterAllNotificationApplicationServices();

            services.AddHttpClient();
            return services;
        }
    }
}

using DomainService.Workflow.Repositories;
using DomainService.Workflow.Services;
using DomainService.Workflow.Nodes;
using DomainService.Workflow.Nodes.ActionAIAgentV1;
using DomainService.Workflow.Nodes.ActionSendMailV1;
using DomainService.Workflow.Nodes.ActionHttpRequestV1;
using DomainService.Workflow.Nodes.ActionDataV1;
using Microsoft.Extensions.DependencyInjection;
using Blocks.Extension.DependencyInjection;
using DomainService.Workflow.Nodes.TriggerEmailV1;
using DomainService.Workflow.Nodes.TriggerDataV1;
using DomainService.Workflow.Nodes.LogicIFV1;
using DomainService.Workflow.Nodes.TriggerWebhookV1;
using DomainService.Workflow.Nodes.TransformSetFieldV1;
using DomainService.Workflow.Nodes.TransformCodeV1;
using DomainService.MagicLink.Service;
using DomainService.Utilities;

namespace DomainService.Workflow
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

            // register repositories
            services.AddSingleton<IWorkflowRepository, WorkflowRepository>();
            services.AddSingleton<IWorkflowVersionRepository, WorkflowVersionRepository>();
            services.AddSingleton<IWorkflowExecutionRepository, WorkflowExecutionRepository>();

            //  rigister node executors

            // Trigger nodes
            services.AddSingleton<INodeExecutor, TriggerWebhookV1Node>();
            services.AddSingleton<INodeExecutor, TriggerEmailV1Node>();
            services.AddSingleton<INodeExecutor, TriggerDataV1Node>();

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

using Blocks.Genesis;
using Workflow.DomainService.Repositories;

namespace Workflow.DomainService.Services
{
    /// <summary>
    /// The three stores an execution is read from and written to: the workflow, its published versions and the
    /// executions. Grouped so <see cref="WorkflowExecutionService"/> takes them as one dependency (Sonar S107).
    /// </summary>
    public sealed class WorkflowExecutionStores
    {
        public WorkflowExecutionStores(
            IWorkflowRepository workflows,
            IWorkflowExecutionRepository executions,
            IWorkflowVersionRepository versions)
        {
            ArgumentNullException.ThrowIfNull(workflows);
            ArgumentNullException.ThrowIfNull(executions);
            ArgumentNullException.ThrowIfNull(versions);

            Workflows = workflows;
            Executions = executions;
            Versions = versions;
        }

        public IWorkflowRepository Workflows { get; }

        public IWorkflowExecutionRepository Executions { get; }

        public IWorkflowVersionRepository Versions { get; }
    }

    /// <summary>
    /// What a trigger needs about its caller: the webhook's auth check, the delegation grant that carries the
    /// caller into an in-process run (and its release), and the webhook body intake. Grouped so
    /// <see cref="WorkflowExecutionService"/> takes them as one dependency (Sonar S107).
    /// </summary>
    public sealed class WorkflowTriggerAccess
    {
        public WorkflowTriggerAccess(
            IWorkflowAuthService auth,
            IDelegationGrantFactory grants,
            IInProcessGrantReleaser grantReleaser,
            IWebhookIntake webhookIntake)
        {
            ArgumentNullException.ThrowIfNull(auth);
            ArgumentNullException.ThrowIfNull(grants);
            ArgumentNullException.ThrowIfNull(grantReleaser);
            ArgumentNullException.ThrowIfNull(webhookIntake);

            Auth = auth;
            Grants = grants;
            GrantReleaser = grantReleaser;
            WebhookIntake = webhookIntake;
        }

        public IWorkflowAuthService Auth { get; }

        public IDelegationGrantFactory Grants { get; }

        public IInProcessGrantReleaser GrantReleaser { get; }

        public IWebhookIntake WebhookIntake { get; }
    }
}

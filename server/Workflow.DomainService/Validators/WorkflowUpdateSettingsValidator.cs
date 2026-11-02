using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentValidation;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Utils;

namespace Workflow.DomainService.Validators
{
    /// <summary>
    /// Rules for PUT UpdateSettings. Error keys are the property paths (e.g. <c>Settings.Alert.WorkflowId</c>),
    /// which the settings modal shows under the matching field. Add a new setting's rules here.
    /// </summary>
    public class WorkflowUpdateSettingsValidator : AbstractValidator<WorkflowUpdateSettingsRequestDto>
    {
        public WorkflowUpdateSettingsValidator(IWorkflowRepository workflowRepository)
        {
            RuleFor(x => x.ItemId).NotEmpty();

            // Only an enabled alert is checked: a paused one keeps whatever target it had.
            When(x => x.Settings?.Alert is { IsEnabled: true }, () =>
            {
                RuleFor(x => x.Settings!.Alert!.Mode)
                    .Must(mode => mode == WorkflowAlertMode.Workflow).WithMessage("Unsupported alert mode.");

                RuleFor(x => x.Settings!.Alert!.WorkflowId)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty().WithMessage("Select a workflow.")
                    .Must((dto, id) => id != dto.ItemId).WithMessage("A workflow can't handle its own failures.")
                    // One read of the target for the remaining checks.
                    .CustomAsync(async (id, context, _) =>
                    {
                        var target = await workflowRepository.GetWorkflowAsync(BlocksContext.GetContext().TenantId, id!);
                        if (target == null)
                            context.AddFailure("Workflow not found.");
                        else if (!HasErrorTrigger(target))
                            context.AddFailure("The selected workflow has no Error Trigger.");
                    });
            });
        }

        // The same rule as the picker: the published version when there is one, else the draft.
        private static bool HasErrorTrigger(WorkflowEntity target)
        {
            var nodes = target.IsPublished ? target.PublishedMeta?.TriggerNodes : target.Nodes;
            return nodes?.Any(n => n.Type == LogicConstants.ErrorTriggerNodeType && n.Category == LogicConstants.TriggerCategory) == true;
        }
    }
}

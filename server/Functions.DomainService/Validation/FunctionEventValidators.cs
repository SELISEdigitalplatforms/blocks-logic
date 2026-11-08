using FluentValidation;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Events;

namespace Functions.DomainService.Validation
{
    /// <summary>
    /// The event settings of a function's draft (its trigger action names). Every message starts with the field it is
    /// about (<c>triggerEvents[0]: …</c>), so the editor can put it next to the input.
    /// </summary>
    public class SaveEventSettingsRequestValidator : AbstractValidator<SaveEventSettingsRequestDto>
    {
        public SaveEventSettingsRequestValidator()
        {
            RuleFor(x => x.FunctionId).NotEmpty();
            RuleFor(x => x.TriggerEvents).NotNull();

            RuleFor(x => x.TriggerEvents)
                .Custom((names, context) =>
                {
                    if (names.Count > FunctionEventCatalog.MaxTriggerEvents)
                    {
                        context.AddFailure($"triggerEvents: at most {FunctionEventCatalog.MaxTriggerEvents} action");
                    }

                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < names.Count; i++)
                    {
                        if (!FunctionEventCatalog.IsValidName(names[i]))
                        {
                            context.AddFailure($"triggerEvents[{i}]: the name must match ^[a-z][a-z0-9_.-]{{2,63}}$");
                        }
                        else if (!seen.Add(names[i]))
                        {
                            context.AddFailure($"triggerEvents[{i}]: the name is listed twice");
                        }
                    }
                })
                .When(x => x.TriggerEvents is not null);
        }
    }
}

using FluentValidation;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Events;

namespace Functions.DomainService.Validation
{
    /// <summary>
    /// The events of a function's draft. Every message starts with the field it is about
    /// (<c>events[0].payload: …</c>), so the editor can put it next to the input.
    /// </summary>
    public class SaveEventSettingsRequestValidator : AbstractValidator<SaveEventSettingsRequestDto>
    {
        private const int MaxShown = 64;

        public SaveEventSettingsRequestValidator()
        {
            RuleFor(x => x.FunctionId).NotEmpty();
            RuleFor(x => x.Events).NotNull();
            RuleFor(x => x.TriggerEvents).NotNull();

            RuleFor(x => x.Events)
                .Custom((events, context) =>
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < events.Count; i++)
                    {
                        foreach (var message in CheckEvent(events[i], i, seen)) context.AddFailure(message);
                    }
                })
                .When(x => x.Events is not null);

            RuleFor(x => x.TriggerEvents)
                .Custom((names, context) =>
                {
                    if (names.Count > FunctionEventCatalog.MaxTriggerEvents)
                    {
                        context.AddFailure($"triggerEvents: at most {FunctionEventCatalog.MaxTriggerEvents} event names");
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

        private static IEnumerable<string> CheckEvent(FunctionEventSettingDto item, int index, HashSet<string> seenTypes)
        {
            if (item is null)
            {
                yield return $"events[{index}]: the event is empty";
                yield break;
            }

            if (FunctionEventCatalog.Find(item.Type) is null)
            {
                var shown = (item.Type ?? string.Empty).Length > MaxShown ? item.Type![..MaxShown] : item.Type;
                yield return $"events[{index}].type: '{shown}' is not an event this platform offers";
                yield break;
            }

            if (!seenTypes.Add(item.Type))
            {
                yield return $"events[{index}].type: '{item.Type}' is listed twice";
            }

            var problem = FunctionEventCatalog.Validate(item.Type, item.Payload, forSend: false);
            if (problem is not null) yield return $"events[{index}].payload: {problem}";
        }
    }
}

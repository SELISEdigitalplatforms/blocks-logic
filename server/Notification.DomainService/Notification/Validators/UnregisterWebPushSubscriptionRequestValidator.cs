using FluentValidation;

namespace DomainService.Notification
{
    public class UnregisterWebPushSubscriptionRequestValidator : AbstractValidator<UnregisterWebPushSubscriptionRequest>
    {
        public UnregisterWebPushSubscriptionRequestValidator()
        {
            RuleFor(x => x.Endpoint)
                .NotEmpty().WithMessage("required");
        }
    }
}

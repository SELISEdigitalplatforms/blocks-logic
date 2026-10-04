using FluentValidation;

namespace DomainService.Notification
{
    public class RegisterWebPushSubscriptionRequestValidator : AbstractValidator<RegisterWebPushSubscriptionRequest>
    {
        public RegisterWebPushSubscriptionRequestValidator()
        {
            RuleFor(x => x.Endpoint)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("required")
                .Must(BeAbsoluteHttpUrl).WithMessage("must be an absolute http(s) URL");

            RuleFor(x => x.Keys)
                .NotNull().WithMessage("required");

            RuleFor(x => x.Keys.P256dh)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("required")
                .When(x => x.Keys != null);

            RuleFor(x => x.Keys.Auth)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("required")
                .When(x => x.Keys != null);
        }

        private static bool BeAbsoluteHttpUrl(string endpoint) =>
            Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}

using FluentValidation;

namespace DomainService.Notification
{
    public class RegisterWebPushSubscriptionRequestValidator : AbstractValidator<RegisterWebPushSubscriptionRequest>
    {
        private const string RequiredMessage = "required";

        public RegisterWebPushSubscriptionRequestValidator()
        {
            RuleFor(x => x.Endpoint)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage(RequiredMessage)
                .Must(BeAbsoluteHttpUrl).WithMessage("must be an absolute http(s) URL");

            RuleFor(x => x.Keys)
                .NotNull().WithMessage(RequiredMessage);

            RuleFor(x => x.Keys.P256dh)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage(RequiredMessage)
                .When(x => x.Keys != null);

            RuleFor(x => x.Keys.Auth)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage(RequiredMessage)
                .When(x => x.Keys != null);
        }

        private static bool BeAbsoluteHttpUrl(string endpoint) =>
            Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}

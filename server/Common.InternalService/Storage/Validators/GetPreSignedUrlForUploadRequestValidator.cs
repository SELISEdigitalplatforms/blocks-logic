using FluentValidation;

namespace Common.InternalService.Storage
{
    public class GetPreSignedUrlForUploadRequestValidator : AbstractValidator<GetPreSignedUrlForUploadRequest>
    {
        public GetPreSignedUrlForUploadRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .WithMessage("Name is required.");

            RuleFor(x => x.ConfigurationName)
                .NotEmpty()
                .WithMessage("Configuration Name should not be empty. Remove this field only if you wish to use the default configuration.")
                .When(x => x.ConfigurationName != null);
        }
    }
}

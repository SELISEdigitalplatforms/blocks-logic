using FluentValidation;

namespace Common.InternalService.Monitor
{
    public class UpdateMonitorConfigurationRequestValidator : AbstractValidator<UpdateMonitorConfigurationRequest>
    {
        private readonly IMonitorConfigurationRepoService _monitorConfigurationRepoService;

        public UpdateMonitorConfigurationRequestValidator(IMonitorConfigurationRepoService monitorConfigurationRepoService)
        {
            _monitorConfigurationRepoService = monitorConfigurationRepoService;

            RuleFor(x => x.ItemId)
                .NotEmpty().WithMessage("ItemId is required for update.");

            RuleFor(x => x.Url)
                .NotEmpty().WithMessage("URL is required.")
                .Must(IsValidUrl).WithMessage("URL is not valid or unreachable.")
                .MustAsync(BeUniqueUrlForUpdate).WithMessage("A monitor with this URL already exists.")
                .When(x => !string.IsNullOrWhiteSpace(x.Url));
        }

        private static bool IsValidUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uriResult))
                return false;

            if (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps)
                return false;
            return true;
        }

        private async Task<bool> BeUniqueUrlForUpdate(UpdateMonitorConfigurationRequest request, string url, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(url))
                return true;

            var existing = await _monitorConfigurationRepoService.GetByUrlAsync(url);

            if (existing == null)
                return true;

            return existing.ItemId == request.ItemId;
        }
    }
}

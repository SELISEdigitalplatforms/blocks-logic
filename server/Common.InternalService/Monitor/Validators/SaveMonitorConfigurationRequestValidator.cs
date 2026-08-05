using FluentValidation;

namespace Common.InternalService.Monitor
{
    public class SaveMonitorConfigurationRequestValidator : AbstractValidator<SaveMonitorConfigurationRequest>
    {
        private readonly IMonitorConfigurationRepoService _monitorConfigurationRepoService;

        public SaveMonitorConfigurationRequestValidator(IMonitorConfigurationRepoService monitorConfigurationRepoService)
        {
            _monitorConfigurationRepoService = monitorConfigurationRepoService;
            ClassLevelCascadeMode = CascadeMode.Stop;
            RuleLevelCascadeMode = CascadeMode.Stop;

            RuleFor(x => x.ProjectKey)
                .NotEmpty().WithMessage("ProjectKey is required.")
                .MustAsync(HaveLessThanMaxMonitors).WithMessage("You cannot have more than 10 monitors for this project.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Monitor name is required.");

            RuleFor(x => x.Url)
                .NotEmpty().WithMessage("URL is required.")
                .Must(IsValidUrl).WithMessage("URL is not valid or unreachable.")
                .MustAsync(BeUniqueUrl).WithMessage("A monitor with this URL already exists.");
        }

        private static bool IsValidUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uriResult))
                return false;

            if (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps)
                return false;

            return true;
        }

        private async Task<bool> HaveLessThanMaxMonitors(string projectKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(projectKey))
                return true;

            var monitors = await _monitorConfigurationRepoService.GetConfigurationListByTenantIdAsync(projectKey);
            return monitors.Count <= 10;
        }

        private async Task<bool> BeUniqueUrl(SaveMonitorConfigurationRequest request, string url, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(url))
                return true;
            var existing = await _monitorConfigurationRepoService.GetByUrlAsync(url);
            return existing == null;
        }
    }
}

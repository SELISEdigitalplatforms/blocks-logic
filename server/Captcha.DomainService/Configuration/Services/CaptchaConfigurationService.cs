using Blocks.Genesis;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Captcha.DomainService.Configuration
{
    public class CaptchaConfigurationService : ICaptchaConfigurationService
    {
        private readonly ICaptchaConfigurationRepository _repository;
        private readonly ILogger<CaptchaConfigurationService> _logger;

        public CaptchaConfigurationService(ICaptchaConfigurationRepository repository,
                                    ILogger<CaptchaConfigurationService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task<CaptchaConfiguration> GetByNameAsync(string configurationName)
        {
            return await _repository.GetByProviderAsync(configurationName);
        }

        public async Task<CaptchaConfiguration> GetCaptchaConfigurationAsync()
        {
            return await _repository.GetCaptchaConfigurationAsync();
        }
    }
}

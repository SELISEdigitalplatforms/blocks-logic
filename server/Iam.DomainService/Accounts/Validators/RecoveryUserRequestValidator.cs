using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Configuration;
using FluentValidation;
using Iam.DomainService.Accounts;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using System.Security.Claims;

public class RecoveryUserRequestValidator : AbstractValidator<RecoveryUserRequest>
{
    private readonly ICaptchaService _captchaService;
    private IDbContextProvider _dbContextProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RecoveryUserRequestValidator(ICaptchaService captchaService,
                                        IDbContextProvider dbContextProvider,
                                        IHttpContextAccessor httpContextAccessor)
    {
        _captchaService = captchaService;
        _dbContextProvider = dbContextProvider;
        _httpContextAccessor = httpContextAccessor;

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Invalid email format.");

        RuleFor(x => x.CaptchaCode)
               .Cascade(CascadeMode.Stop)
               .MustAsync(MustMatchCaptcha).WithMessage("Captcha doesn't match")
               .When(x => !string.IsNullOrWhiteSpace(x.CaptchaCode));
    }

    private async Task<bool> MustMatchCaptcha(string captchaCode, CancellationToken cancellationToken)
    {
        var configurationName = (await GetCaptchaConfig())?.Provider ?? "";
        var verifyCaptchaQueryResponse = await _captchaService.VerifyCaptchaAsync(new VerifyCaptchaRequest { VerificationCode = captchaCode, ConfigurationName = configurationName });

        return verifyCaptchaQueryResponse.Verified;
    }

    private async Task<CaptchaConfiguration> GetCaptchaConfig()
    {
        var captchaConfiguration = _dbContextProvider.GetCollection<CaptchaConfiguration>("CaptchaConfigurations");
        var configuration = await (await captchaConfiguration.FindAsync(Builders<CaptchaConfiguration>.Filter.Eq(mc => mc.IsEnable, true))).FirstOrDefaultAsync();
        return configuration;
    }
}

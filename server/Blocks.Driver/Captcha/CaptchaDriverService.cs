using Captcha.DomainService.Captcha;

namespace Blocks.Driver.Captcha;

public class CaptchaDriverService : ICaptchaDriverService
{
    private readonly ICaptchaService _captchaService;

    public CaptchaDriverService(ICaptchaService captchaService)
    {
        _captchaService = captchaService;
    }

    public CreateCaptchaRequestResponse Create(CreateCaptchaRequest command)
        => _captchaService.CreateCaptcha(command);

    public Task<SubmitCaptchaRequestResponse> Submit(SubmitCaptchaRequest command)
        => _captchaService.SubmitCaptchaAsync(command);

    public Task<VerifyCaptchaRequestResponse> Verify(VerifyCaptchaRequest query)
        => _captchaService.VerifyCaptchaAsync(query);
}

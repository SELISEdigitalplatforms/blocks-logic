using Captcha.DomainService.Captcha;

namespace Blocks.Driver.Captcha;

/// <summary>
/// Service for handling CAPTCHA operations including creation, submission, and verification.
/// </summary>
public interface ICaptchaDriverService
{
    /// <summary>
    /// Creates a new CAPTCHA based on the provided request.
    /// </summary>
    CreateCaptchaRequestResponse Create(CreateCaptchaRequest command);
    /// <summary>
    /// Submits a CAPTCHA and generates a verification code.
    /// </summary>
    Task<SubmitCaptchaRequestResponse> Submit(SubmitCaptchaRequest command);
    /// <summary>
    /// Verifies a CAPTCHA using the provided verification code and configuration name.
    /// </summary>
    Task<VerifyCaptchaRequestResponse> Verify(VerifyCaptchaRequest query);
}

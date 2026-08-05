using Mfa.DomainService.Configuration;
using Mfa.DomainService.Shared;

namespace Blocks.Driver.Mfa;

/// <summary>
/// Service for generating and verifying MFA one-time passwords.
/// </summary>
public interface IMfaDriverService
{
    /// <summary>
    /// Generate OTP
    /// </summary>
    Task<OtpGenerationResponse> GenerateOtpAsync(OtpGenerationRequest request);
    /// <summary>
    /// Verify OTP
    /// </summary>
    Task<OtpVerificationResponse> VerifyOtpAsync(VerifyOtpRequest request);
}

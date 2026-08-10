using Mfa.DomainService.Configuration;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;

namespace Blocks.Driver.Mfa;

public class MfaDriverService : IMfaDriverService
{
    private readonly IMfaManagementService _mfaManagementService;

    public MfaDriverService(IMfaManagementService mfaManagementService)
    {
        _mfaManagementService = mfaManagementService;
    }

    public Task<OtpGenerationResponse> GenerateOtpAsync(OtpGenerationRequest request)
        => _mfaManagementService.GenerateOTPAsync(request);

    public Task<OtpVerificationResponse> VerifyOtpAsync(VerifyOtpRequest request)
        => _mfaManagementService.VerifyOTPAsync(request);
}

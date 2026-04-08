namespace Iam.DomainService.Accounts
{
    public interface IAccountService
    {
        Task<BaseAccountResponse> ActivateAccountAsync(ActivateUserRequest activateUserRequest);
        Task<BaseAccountResponse> RecoverAccountAsync(RecoveryUserRequest recoveryRequest);
        Task<BaseAccountResponse> ResetAccountPasswordAsync(ResetPasswordRequest resetPasswordRequest);
        Task<BaseAccountResponse> ChangePasswordAsync(ChangePasswordRequest changePasswordRequest);
        Task<BaseAccountResponse> ResendActivationAsync(ResendActivationRequest resendActivationRequest);
        Task<ActivationCodeValidationResponse> ValidateAccountActivationCodeAsync(ValidateActivationCodeRequest validateActivationCodeRequest);
    }
}

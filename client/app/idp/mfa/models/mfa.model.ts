export interface IResendMfaOtpPayload {
  mfaId: string;
  sendPhoneNumberAsEmailDomain?: string;
}

export interface IVerifyMfaOtpResponse {
  errors: unknown;
  isSuccess: boolean;
  isValid: boolean;
  userId: string;
}

import { http } from "@/lib/http-client";
import {
  IResendMfaOtpPayload,
  IVerifyMfaOtpResponse,
} from "../models/mfa.model";
import { MFA_ENDPOINTS } from "../constants/endpoint.constant";

export class MFAService {
  resendOtp(payload: IResendMfaOtpPayload): Promise<IVerifyMfaOtpResponse> {
    return http.post(MFA_ENDPOINTS.RESEND_OTP, payload.mfaId);
  }
}

export const mfaService = new MFAService();

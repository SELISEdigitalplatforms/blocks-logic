import { API_BASES } from "@/constants/endpoint.constant";

// ─── MFA endpoints (mfa.service — IDP & MFA bases) ─────────────────────────

const MFA_SUBPATH = "/Mfa";

export const MFA_ENDPOINTS = {
  RESEND_OTP: `${API_BASES.IDP}${MFA_SUBPATH}/ResendOtp`,
} as const;

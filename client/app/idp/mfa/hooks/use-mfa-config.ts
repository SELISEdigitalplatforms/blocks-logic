import { useMutation } from "@tanstack/react-query";
import { mfaService } from "../services/mfa.service";

export const useResendMfaOTP = () => {
  return useMutation({
    mutationKey: ["mfa-config", "resend-otp"],
    mutationFn: mfaService.resendOtp,
  });
};

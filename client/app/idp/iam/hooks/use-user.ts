import { useAuthStore } from "@/store/useAuthStore";
import {
  IGetSignUpSettingPayload,
} from "@blocks-idp/iam/models/user";
import { userService } from "@blocks-idp/iam/services/user.service";
import { useQuery } from "@tanstack/react-query";

export const useGetUser = (options?: { enabled?: boolean }) => {
  const authStore = useAuthStore();
  return useQuery({
    queryKey: ["user"],
    queryFn: async () => {
      const user = await userService.getUser();
      authStore.setUser(user.data);
      return user;
    },
    ...options,
  });
};

export const useGetSignUpSetting = (
  option: IGetSignUpSettingPayload,
  options?: { enabled?: boolean },
) => {
  return useQuery({
    queryKey: ["sign-up-setting", option],
    queryFn: () => userService.getSignUpSetting(option),
    ...options,
  });
};


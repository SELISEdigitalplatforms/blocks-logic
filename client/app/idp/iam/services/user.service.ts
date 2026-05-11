import { http } from "@/lib/http-client";
import {
  IAccountResendActivationPayload,
  IAccountResendActivationResponse,
  IGetSignUpSettingPayload,
  IGetSignUpSettingResponse,
  User,
} from "@blocks-idp/iam/models/user";
import { UserAccountService } from "./account.service";
import { USER_ENDPOINTS } from "../constants/endpoint.constant";

export class UserService {
  constructor(public account: UserAccountService) {}

  getUser(): Promise<{ data: User }> {
    return http.get(`${USER_ENDPOINTS.GET_USER}`, undefined, {
      absoluteUrl: true,
    });
  }

  getSignUpSetting(
    payload: IGetSignUpSettingPayload,
  ): Promise<IGetSignUpSettingResponse> {
    return http.get(
      `${USER_ENDPOINTS.GET_SIGNUP_SETTING}?ProjectKey=${payload.projectKey}`,
    );
  }
  accountDeactivate(
    payload: IAccountResendActivationPayload,
  ): Promise<IAccountResendActivationResponse> {
    return http.post(USER_ENDPOINTS.DEACTIVATE, payload);
  }
}

export const userService = new UserService(new UserAccountService());

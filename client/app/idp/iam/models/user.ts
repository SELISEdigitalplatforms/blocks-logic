export interface User {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  language: string;
  salutation: string;
  firstName: string;
  lastName: string;
  email: string;
  userName: string;
  phoneNumber: string;
  roles: string[];
  permissions: string[];
  active: boolean;
  isVarified: boolean;
  profileImageUrl: string;
  mfaEnabled: boolean;
  lastLoggedInTime: string;
  logInCount: number;
  firstLoggedInTime: string;
  userMfaType: number;
  isMfaVerified: boolean;
  userCreationType: number;
  memberships: IMembership[]
}

export interface IMembership {
  organizationId: string,
  roles: string[],
  permissions: string[]
}

export const status = [
  {
    value: "Active",
    label: "Active",
  },
  {
    value: "Inactive",
    label: "Inactive",
  },
  {
    value: "Verified",
    label: "Verified",
  },
];

export interface IAccountActivationPayload {
  code: string;
  firstname: string;
  lastname: string;
  password: string;
  captchaCode?: string;
  mailPurpose?: string;
  preventPostEvent: boolean;
  projectKey: string;
}

export interface IAccountActivationResponse {
  errors: unknown | null;
  isSuccess: boolean;
}

export interface IAccountResendActivationPayload {
  userId: string;
  // mailPurpose: string;
  projectKey: string;
}
export interface IAccountResendActivationResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface IAccountRecoverPayload {
  email: string;
  captchaCode?: string;
  mailPurpose?: string;
  projectKey: string;
}
export interface IAccountRecoverResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface IAccountResetPasswordPayload {
  code: string;
  password: string;
  captchaCode?: string;
  logoutFromAllDevices: boolean;
  projectKey: string;
}
export interface IAccountResetPasswordResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface IActivationCodeValidationPayload {
  activationCode: string;
  projectKey: string;
}

export interface IActivationCodeExpirationResponse {
  errors: unknown | null;
  isSuccess: boolean;
  userId: string;
}

export interface IGetSignUpSettingPayload {
  projectKey: string;
  // itemId: string;
}

export interface IGetSignUpSettingResponse {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  isEmailPasswordSignUpEnabled: boolean;
  isSSoSignUpEnabled: boolean;
}

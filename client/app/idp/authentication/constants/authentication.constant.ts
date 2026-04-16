export const UserCreationType: Record<number, string> = {
  0: "None",
  1: "Portal",
  2: "API",
  3: "Service",
  4: "Social",
};

export enum GRANT_TYPES {
  password = "password",
  social = "social",
  clientCredential = "client_credential",
  authorizationCode = "authorization_code",
}

export const GRANT_TYPES_OPTIONS: { id: GRANT_TYPES; label: string; value: string }[] = [
  { id: GRANT_TYPES.password, label: "Email/Password", value: GRANT_TYPES.password },
  { id: GRANT_TYPES.social, label: "SSO", value: GRANT_TYPES.social },
  {
    id: GRANT_TYPES.clientCredential,
    label: "Client Credential",
    value: GRANT_TYPES.clientCredential,
  },
  {
    id: GRANT_TYPES.authorizationCode,
    label: "Authorization Code",
    value: GRANT_TYPES.authorizationCode,
  },
];

export const AuthenticationTabs: { id: string; label: string; value: string }[] = [
  { id: "general", label: "General", value: "general" },
  // {
  //   id: GRANT_TYPES.clientCredential,
  //   label: "Client Credential",
  //   value: GRANT_TYPES.clientCredential,
  // },
];

export const SecretManagementTabs: { id: string; label: string; value: string }[] = [
  {
    id: GRANT_TYPES.authorizationCode,
    label: "OIDC",
    value: GRANT_TYPES.authorizationCode,
  },
  {
    id: GRANT_TYPES.social,
    label: "SSO",
    value: GRANT_TYPES.social,
  },
  {
    id: "external-idp",
    label: "External IdP",
    value: "external-idp",
  },
];

export const providers = [
  { id: "keycloak", name: "Keycloak", icon: "/assets/images/keycloak_icon.png" },
  { id: "okta", name: "Okta", icon: "/assets/images/okta_symbol.png" },
  { id: "auth0", name: "Auth0", icon: "/assets/images/auth0.png" },
  { id: "azure", name: "Azure", icon: "/assets/images/azure.png" },
  { id: "others", name: "Others", icon: "" },
];

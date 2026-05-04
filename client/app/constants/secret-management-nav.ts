import {
  Server,
  KeyRound,
  Layers,
  ShieldCheck,
  Users,
  Globe,
  ShieldAlert,
  Smartphone,
  Link2,
  Mail,
  Bell,
  Database,
  BrainCircuit,
  type LucideIcon,
} from "lucide-react";
import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";

export type NavItem = {
  id: string;
  label: string;
  value: string;
  icon: LucideIcon;
  desc: string;
};

export type NavGroup = {
  label: string;
  icon?: LucideIcon;
  items: NavItem[];
};

export const SECRET_MANAGEMENT_NAV_GROUPS: NavGroup[] = [
  {
    label: "Secrets & Keys",
    items: [
      { id: "infra-config", label: "Infra Config", value: "infra-config", icon: Server, desc: "Manage infrastructure configurations" },
      { id: "my-secret", label: "My Secret", value: "my-secret", icon: KeyRound, desc: "Manage your secrets and credentials" },
      { id: "managed-services", label: "My Service", value: "managed-services", icon: Layers, desc: "Manage connected services" },
    ],
  },
  {
    label: "Authentication",
    items: [
      { id: GRANT_TYPES.authorizationCode, label: "OIDC", value: GRANT_TYPES.authorizationCode, icon: ShieldCheck, desc: "OpenID Connect configuration" },
      { id: GRANT_TYPES.social, label: "SSO", value: GRANT_TYPES.social, icon: Users, desc: "Single sign-on providers" },
      { id: "external-idp", label: "External IdP", value: "external-idp", icon: Globe, desc: "External identity providers & certificates" },
      { id: "captcha", label: "Captcha", value: "captcha", icon: ShieldAlert, desc: "Bot protection configuration" },
      { id: "mfa", label: "MFA", value: "mfa", icon: Smartphone, desc: "Multi-factor authentication settings" },
      { id: "magic-url", label: "Magic URL", value: "magic-url", icon: Link2, desc: "Passwordless magic link settings" },
    ],
  },
  {
    label: "Communication",
    items: [
      { id: "email", label: "Email", value: "email", icon: Mail, desc: "Email provider configuration" },
      { id: "notification", label: "Notification", value: "notification", icon: Bell, desc: "Push & notification settings" },
    ],
  },
  {
    label: "Intelligence",
    items: [
      { id: "storage", label: "Storage", value: "storage", icon: Database, desc: "File and object storage" },
      { id: "ai-models", label: "AI Models", value: "ai-models", icon: BrainCircuit, desc: "AI model integrations" },
    ],
  },
];

export const ALL_SECRET_MANAGEMENT_NAV_ITEMS = SECRET_MANAGEMENT_NAV_GROUPS.flatMap((g) => g.items);

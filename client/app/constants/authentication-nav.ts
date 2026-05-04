import {
  Wrench,
  LogIn,
  UserPlus,
  Mail,
  FileCode,
  ShieldCheck,
  Lock,
  type LucideIcon,
} from "lucide-react";
import { NavItem, NavGroup } from "@/constants/secret-management-nav";

export type { NavItem, NavGroup };

export const AUTHENTICATION_NAV_GROUPS: NavGroup[] = [
  {
    label: "Configuration",
    items: [
      { id: "general", label: "General", value: "general", icon: Wrench, desc: "General authentication settings" },
      { id: "signin-flow", label: "Signin Flow", value: "signin-flow", icon: LogIn, desc: "Configure signin flow" },
      { id: "signup-flow", label: "Signup Flow", value: "signup-flow", icon: UserPlus, desc: "Configure signup flow" },
    ],
  },
  {
    label: "Templates",
    items: [
      { id: "email-template", label: "Email Template", value: "email-template", icon: Mail, desc: "Manage email templates" },
      { id: "oidc-template", label: "OIDC Template", value: "oidc-template", icon: FileCode, desc: "Configure OIDC template" },
    ],
  },
  {
    label: "Access Control",
    items: [
      { id: "roles", label: "Roles", value: "roles", icon: ShieldCheck, desc: "Manage roles" },
      { id: "permissions", label: "Permissions", value: "permissions", icon: Lock, desc: "Manage permissions" },
    ],
  },
];

export const ALL_AUTHENTICATION_NAV_ITEMS = AUTHENTICATION_NAV_GROUPS.flatMap((g) => g.items);

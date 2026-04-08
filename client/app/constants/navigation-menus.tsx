import { ShieldCheck } from "lucide-react";
import { Menu } from "@/models/menu-models";

export const navigationMenus: Menu[] = [
  {
    type: "menu",
    id: "service-identity",
    name: "Identity",
    path: "/services/authentication",
    icon: ShieldCheck,
    children: [
      {
        type: "menu",
        id: "service-identity__authentication",
        name: "Authentication",
        path: "/services/authentication",
      },
      {
        type: "menu",
        id: "service-identity__authorization",
        name: "Access Manager",
        path: "/services/iam",
      },
      {
        type: "menu",
        id: "service-identity__mfa",
        name: "MFA",
        path: "/services/mfa",
      },
      {
        type: "menu",
        id: "service-identity__captcha",
        name: "Captcha",
        path: "/services/captcha",
      },
    ],
  },
];
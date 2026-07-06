import { Menu } from "@/models/menu-models";
import {
  Home,
  Package,
  Workflow,
} from "lucide-react";

export const navigationMenus: Menu[] = [
  {
    id: "overview-project",
    type: "menu",
    name: "Overview",
    path: "/app/dashboard",
    icon: Home,
  },
  {
    type: "separator",
    id: "separator-overview",
  },
  {
    id: "environments",
    type: "menu",
    name: "Environments",
    path: "/app/project-overview/environments",
    icon: Package,
  },
  {
    type: "separator",
    id: "separator-identity",
  },
  {
    id: "service-workflow",
    type: "menu",
    name: "Workflow",
    path: "/app/workflow",
    icon: Workflow,
  },
];

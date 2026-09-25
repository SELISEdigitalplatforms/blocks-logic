import { Menu } from "@/models/menu-models";
import { CalendarClock, Home, Waypoints, Workflow } from "lucide-react";

/** The proxy icon, the same one the workflow Proxy node uses. */
export const ProxyIcon = Waypoints;

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
  { id: "environments", type: "menu", name: "Environments", path: "/app/project/environments" },
  {
    type: "separator",
    id: "separator-identity",
  },
  {
    id: "service-schedule",
    type: "menu",
    name: "Schedules",
    path: "/app/schedule",
    icon: CalendarClock,
  },
  {
    id: "service-workflow",
    type: "menu",
    name: "Workflow",
    path: "/app/workflow",
    icon: Workflow,
  },
  {
    id: "service-proxy",
    type: "menu",
    name: "Proxy",
    path: "/app/proxy",
    icon: ProxyIcon,
  },
];

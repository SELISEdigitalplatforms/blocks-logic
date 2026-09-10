import { Menu } from "@/models/menu-models";
import * as React from "react";
import {
  CalendarClock,
  FunctionSquare,
  Home,
  type LucideIcon,
  type LucideProps,
  Workflow,
} from "lucide-react";

export const ProxyIcon = React.forwardRef<SVGSVGElement, LucideProps>(
  ({ color = "currentColor", size = 20, strokeWidth = 2.2, ...props }, ref) =>
    React.createElement(
      "svg",
      {
        ref,
        xmlns: "http://www.w3.org/2000/svg",
        width: size,
        height: size,
        viewBox: "0 0 24 24",
        fill: "none",
        stroke: color,
        strokeWidth,
        strokeLinecap: "round",
        strokeLinejoin: "round",
        ...props,
      },
      React.createElement("circle", { cx: "5", cy: "12", r: "2.5" }),
      React.createElement("circle", { cx: "19", cy: "12", r: "2.5" }),
      React.createElement("path", { d: "M7.5 12h9" }),
      React.createElement("path", { d: "M12 6.5v11" }),
    ),
) as LucideIcon;

ProxyIcon.displayName = "ProxyIcon";

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
    id: "service-functions",
    type: "menu",
    name: "Functions",
    path: "/app/functions",
    icon: FunctionSquare,
  },
  {
    id: "service-proxy",
    type: "menu",
    name: "Proxy",
    path: "/app/proxy",
    icon: ProxyIcon,
  },
];

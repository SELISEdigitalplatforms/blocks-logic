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
  ({ color = "currentColor", size = 20, strokeWidth = 1.5, ...props }, ref) =>
    React.createElement(
      "svg",
      {
        ref,
        xmlns: "http://www.w3.org/2000/svg",
        width: size,
        height: size,
        viewBox: "0 0 20 20",
        fill: "none",
        stroke: color,
        strokeWidth,
        strokeLinecap: "round",
        strokeLinejoin: "round",
        ...props,
      },
      React.createElement("circle", { cx: "4", cy: "10", r: "1.8" }),
      React.createElement("circle", { cx: "16", cy: "10", r: "1.8" }),
      React.createElement("rect", { x: "8", y: "6.5", width: "4", height: "7", rx: "1.4" }),
      React.createElement("path", { d: "M5.8 10H8" }),
      React.createElement("path", { d: "M12 10H14.2" }),
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

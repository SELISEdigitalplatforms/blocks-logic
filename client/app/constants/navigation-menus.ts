import { Menu } from "@/models/menu-models";
import * as React from "react";
import { CalendarClock, Home, type LucideIcon, type LucideProps, Workflow } from "lucide-react";

/**
 * The `f` of f(x), standing on its own. Lucide's `FunctionSquare` boxes the same glyph in a
 * rounded square, which at menu size reads as "a tile" before it reads as "a function" — and
 * the box squeezes the `f` down to a hairline that is hard to tell from its neighbours at a
 * glance. Without it the glyph gets the whole canvas, and the heavier default stroke carries
 * the visual weight of the Lucide icons around it.
 */
export const FunctionIcon = React.forwardRef<SVGSVGElement, LucideProps>(
  ({ color = "currentColor", size = 20, strokeWidth = 2.2, ...props }, ref) =>
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
      // The stem: up out of the descender, then out again to the top terminal.
      React.createElement("path", {
        d: "M6.4 17.2c2.4 0 3.6-1.2 3.6-3.6V6.4c0-2.4 1.2-3.6 3.6-3.6",
      }),
      // The crossbar, at the x-height.
      React.createElement("path", { d: "M6.4 9.4h7.2" }),
    ),
) as LucideIcon;

FunctionIcon.displayName = "FunctionIcon";

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
    icon: FunctionIcon,
  },
  {
    id: "service-proxy",
    type: "menu",
    name: "Proxy",
    path: "/app/proxy",
    icon: ProxyIcon,
  },
];

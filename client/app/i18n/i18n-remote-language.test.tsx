import { afterEach, describe, expect, it } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { tStatic, useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";
import { ProxyStatusBadge } from "@/modules/proxy/components/proxy-status-badge";
import { buildNavigationMenus } from "@/constants/navigation-menus";
import "./index";

const LOGIC_SHARED_MODULE = "logic.shared";
const OVERVIEW_KEY = "NAVIGATION_MENUS.OVERVIEW";

// A placeholder language and values: the runtime treats every non-English language the same way.
const OTHER = "xx";
const otherText = {
  "PROXY_STATUS_BADGE.LIVE": "xx-live",
  "PROXY_STATUS_BADGE.PAUSED": "xx-paused",
};

describe("text in another project language", () => {
  afterEach(() => {
    act(() => useAppSettingsStore.getState().setSettings({ language: "en" }));
  });

  it("shows English by default", () => {
    render(<ProxyStatusBadge proxy={{ enabled: false }} />);
    expect(screen.getByText("Paused")).toBeTruthy();
  });

  it("shows the other language's text of a component after the language changes", () => {
    useI18nStore.getState().setRemote(OTHER, "logic.proxy.components", otherText);
    render(<ProxyStatusBadge proxy={{ enabled: false }} />);
    act(() => useAppSettingsStore.getState().setSettings({ language: OTHER }));
    expect(screen.getByText("xx-paused")).toBeTruthy();
  });

  it("falls back to English for a key the other language does not have", () => {
    useI18nStore.getState().setRemote(OTHER, LOGIC_SHARED_MODULE, {});
    act(() => useAppSettingsStore.getState().setSettings({ language: OTHER }));
    expect(tStatic(LOGIC_SHARED_MODULE, OVERVIEW_KEY)).toBe("Overview");
  });

  it("translates outside React and picks the plural form", () => {
    useI18nStore.getState().setRemote(OTHER, LOGIC_SHARED_MODULE, {
      [OVERVIEW_KEY]: "xx-overview",
      TABLE_PAGINATION_items_one: "x",
    });
    act(() => useAppSettingsStore.getState().setSettings({ language: OTHER }));
    expect(tStatic(LOGIC_SHARED_MODULE, OVERVIEW_KEY)).toBe("xx-overview");
    const menus = buildNavigationMenus((key) => tStatic(LOGIC_SHARED_MODULE, key));
    expect(menus.some((menu) => menu.type === "menu" && menu.name === "xx-overview")).toBe(true);
  });
});

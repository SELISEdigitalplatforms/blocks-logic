import { afterEach, describe, expect, it } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { tStatic, useAppSettingsStore, useI18nStore } from "@seliseblocks/genesis-os";
import { ProxyStatusBadge } from "@/modules/proxy/components/proxy-status-badge";
import { buildNavigationMenus } from "@/constants/navigation-menus";
import "./index";

const German = { "PROXY_STATUS_BADGE.LIVE": "Live", "PROXY_STATUS_BADGE.PAUSED": "Pausiert" };

describe("German text", () => {
  afterEach(() => {
    act(() => useAppSettingsStore.getState().setSettings({ language: "en" }));
  });

  it("shows English by default", () => {
    render(<ProxyStatusBadge proxy={{ enabled: false }} />);
    expect(screen.getByText("Paused")).toBeTruthy();
  });

  it("shows the German text of a component after the language changes", () => {
    useI18nStore.getState().setRemote("de", "logic.proxy.components", German);
    render(<ProxyStatusBadge proxy={{ enabled: false }} />);
    act(() => useAppSettingsStore.getState().setSettings({ language: "de" }));
    expect(screen.getByText("Pausiert")).toBeTruthy();
  });

  it("falls back to English for a key the German file does not have", () => {
    useI18nStore.getState().setRemote("de", "logic.shared", {});
    act(() => useAppSettingsStore.getState().setSettings({ language: "de" }));
    expect(tStatic("logic.shared", "NAVIGATION_MENUS.OVERVIEW")).toBe("Overview");
  });

  it("translates outside React and picks the plural form", () => {
    useI18nStore.getState().setRemote("de", "logic.shared", {
      "NAVIGATION_MENUS.OVERVIEW": "Übersicht",
      TABLE_PAGINATION_items_one: "x",
    });
    act(() => useAppSettingsStore.getState().setSettings({ language: "de" }));
    expect(tStatic("logic.shared", "NAVIGATION_MENUS.OVERVIEW")).toBe("Übersicht");
    const menus = buildNavigationMenus((key) => tStatic("logic.shared", key));
    expect(menus.some((menu) => menu.type === "menu" && menu.name === "Übersicht")).toBe(true);
  });
});

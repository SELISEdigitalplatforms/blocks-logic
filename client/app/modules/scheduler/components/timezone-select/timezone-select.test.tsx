import { afterEach, describe, expect, it, vi } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";

const zones = vi.hoisted(() => ({ browser: "Asia/Dhaka", list: ["UTC", "Asia/Dhaka", "Asia/Kathmandu", "America/New_York"] }));
vi.mock("../../utils/timezone", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../utils/timezone")>()),
  browserTimeZone: () => zones.browser,
  listTimeZones: () => zones.list,
}));

import { matchZone, TimezoneSelect } from "./timezone-select";

// cmdk measures its list; jsdom has no ResizeObserver or scrollIntoView.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof ResizeObserver;
Element.prototype.scrollIntoView ??= () => undefined;

const trigger = () => screen.getByRole("combobox", { name: "Timezone" });

const renderSelect = (value = "UTC") => {
  const onChange = vi.fn();
  renderWithProviders(<TimezoneSelect value={value} onChange={onChange} />);
  return onChange;
};

describe("TimezoneSelect", () => {
  afterEach(() => {
    zones.browser = "Asia/Dhaka";
    zones.list = ["UTC", "Asia/Dhaka", "Asia/Kathmandu", "America/New_York"];
  });

  it("shows the value with its offset on a combobox trigger", () => {
    renderSelect("Asia/Dhaka");
    const button = trigger();
    expect(button.textContent).toContain("Asia/Dhaka (UTC+06:00)");
    expect(button.getAttribute("aria-expanded")).toBe("false");
  });

  it("lists UTC first, the browser shortcut, then every zone with offsets", async () => {
    renderSelect();
    await userEvent.click(trigger());

    const options = screen.getAllByRole("option").map((option) => option.textContent);
    expect(options[0]).toContain("UTC");
    expect(options[1]).toContain("Use my timezone · Asia/Dhaka");
    expect(options).toEqual(expect.arrayContaining([expect.stringContaining("Asia/KathmanduUTC+05:45")]));
    expect(trigger().getAttribute("aria-expanded")).toBe("true");
  });

  it("filters by a case-insensitive substring and selects", async () => {
    const onChange = renderSelect();
    await userEvent.click(trigger());
    await userEvent.type(screen.getByPlaceholderText("Search timezone…"), "KATHM");

    const options = screen.getAllByRole("option");
    expect(options).toHaveLength(1);
    await userEvent.click(within(options[0]).getByText("Asia/Kathmandu"));
    expect(onChange).toHaveBeenCalledWith("Asia/Kathmandu");
  });

  it("shows the empty message when nothing matches", async () => {
    renderSelect();
    await userEvent.click(trigger());
    await userEvent.type(screen.getByPlaceholderText("Search timezone…"), "mars");
    expect(screen.getByText("No timezone found.")).toBeTruthy();
  });

  it("picks the browser zone from the shortcut", async () => {
    const onChange = renderSelect();
    await userEvent.click(trigger());
    await userEvent.click(screen.getByText("Use my timezone · Asia/Dhaka"));
    expect(onChange).toHaveBeenCalledWith("Asia/Dhaka");
  });

  it("has no shortcut when the browser is on UTC, and still offers UTC without a zone list", async () => {
    zones.browser = "UTC";
    zones.list = ["UTC"];
    const onChange = renderSelect("Asia/Dhaka");
    await userEvent.click(trigger());
    expect(screen.queryByText(/Use my timezone/)).toBeNull();
    await userEvent.click(screen.getAllByRole("option")[0]);
    expect(onChange).toHaveBeenCalledWith("UTC");
  });
});

describe("matchZone", () => {
  it("is a trimmed, case-insensitive substring match", () => {
    expect(matchZone("Asia/Dhaka", " dhaka ")).toBe(1);
    expect(matchZone("Asia/Dhaka", "kolkata")).toBe(0);
  });
});

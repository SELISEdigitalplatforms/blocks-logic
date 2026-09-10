import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { ProxyStatusBadge } from "./proxy-status-badge";

describe("ProxyStatusBadge", () => {
  it("shows live and paused labels from enabled state", () => {
    const { rerender } = renderWithProviders(<ProxyStatusBadge proxy={{ enabled: true }} />);

    expect(screen.getByText("Live")).toBeTruthy();

    rerender(<ProxyStatusBadge proxy={{ enabled: false }} />);
    expect(screen.getByText("Paused")).toBeTruthy();
    expect(screen.getByText("Paused").className).toContain("border-slate-300");
    expect(screen.getByText("Paused").className).toContain("bg-slate-200");
    expect(screen.getByText("Paused").className).toContain("hover:bg-slate-200");
    expect(screen.getByText("Paused").className).toContain("dark:bg-slate-800");
    expect(screen.getByText("Paused").className).toContain("dark:hover:bg-slate-800");
  });
});

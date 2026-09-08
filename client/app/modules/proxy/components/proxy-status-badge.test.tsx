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
    expect(screen.getByText("Paused").className).toContain("bg-amber-100");
  });
});

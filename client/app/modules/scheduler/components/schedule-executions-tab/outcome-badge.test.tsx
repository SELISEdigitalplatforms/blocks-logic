import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { OutcomeBadge } from "./outcome-badge";

describe("OutcomeBadge", () => {
  it("shows an icon and text for a success", () => {
    const { container } = render(<OutcomeBadge outcome="Succeeded" />);
    expect(screen.getByText("Succeeded")).toBeTruthy();
    expect(container.querySelector("svg.text-success")).not.toBeNull();
  });

  it("shows an icon and text for a failure, with dark-aware tokens", () => {
    render(<OutcomeBadge outcome="Failed" />);
    const failed = screen.getByText("Failed");
    expect(failed.className).toContain("bg-blocks-error-100");
    expect(failed.className).toContain("text-blocks-error-800");
    expect(failed.querySelector("svg")).not.toBeNull();
  });
});

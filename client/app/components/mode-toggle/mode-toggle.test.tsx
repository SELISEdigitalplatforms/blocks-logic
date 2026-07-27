import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ModeToggle } from "./mode-toggle";

describe("ModeToggle", () => {
  it("renders a theme toggle button with an accessible label", () => {
    render(<ModeToggle />);
    expect(screen.getByText("Toggle theme")).toBeTruthy();
    expect(screen.getByRole("button")).toBeTruthy();
  });

  it("is clickable", () => {
    render(<ModeToggle />);
    const button = screen.getByRole("button");
    button.click();
    expect(button).toBeTruthy();
  });
});

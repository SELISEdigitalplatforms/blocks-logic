import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { InstallScriptsToggle } from "./install-scripts-toggle";

describe("InstallScriptsToggle", () => {
  it("shows the current value and reports a change", async () => {
    const onChange = vi.fn();
    renderWithProviders(<InstallScriptsToggle checked={false} onChange={onChange} />);
    const toggle = screen.getByRole("switch", { name: "Allow package install scripts" });
    expect(toggle.getAttribute("aria-checked")).toBe("false");
    await userEvent.click(toggle);
    expect(onChange).toHaveBeenCalledWith(true);
  });

  it("explains why it exists", () => {
    renderWithProviders(<InstallScriptsToggle checked onChange={() => {}} />);
    expect(screen.getByText(/native packages such as bcrypt/i)).toBeTruthy();
  });
});

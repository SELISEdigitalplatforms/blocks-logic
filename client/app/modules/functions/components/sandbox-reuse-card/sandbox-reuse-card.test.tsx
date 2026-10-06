import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { SandboxReuseCard } from "./sandbox-reuse-card";

describe("SandboxReuseCard", () => {
  it("is information, not a setting: there is no switch", () => {
    renderWithProviders(<SandboxReuseCard />);
    expect(screen.getByText("Fast calls: the sandbox is reused")).toBeTruthy();
    expect(screen.queryByRole("switch")).toBeNull();
  });

  it("always states the rules a reused sandbox holds the function to", () => {
    renderWithProviders(<SandboxReuseCard />);
    const rules = screen.getByTestId("reuse-rules");
    expect(rules.textContent).toMatch(/module-level variables/);
    expect(rules.textContent).toMatch(/ctx\.waitUntil\(\)/);
  });
});

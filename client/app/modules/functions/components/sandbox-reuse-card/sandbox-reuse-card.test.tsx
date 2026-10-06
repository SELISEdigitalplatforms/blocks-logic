import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { SandboxReuseCard } from "./sandbox-reuse-card";
import { ITriggerConfig } from "../../types/function.types";

const trigger: ITriggerConfig = {
  httpEnabled: true,
  httpMethod: "Post",
  httpMethods: [],
  reuseSandbox: false,
  responseMode: "async",
  authMode: "Token",
  roles: [],
  permissions: [],
  roleMatch: "Any",
  permissionMatch: "Any",
  combine: "Or",
  workflowEnabled: true,
};

describe("SandboxReuseCard", () => {
  it("is off by default and hides the rules", () => {
    renderWithProviders(<SandboxReuseCard value={trigger} onChange={vi.fn()} />);
    expect(screen.getByText("Reuse sandbox (faster)")).toBeTruthy();
    expect(screen.getByRole("switch", { name: "Reuse sandbox" }).getAttribute("aria-checked")).toBe(
      "false",
    );
    expect(screen.queryByTestId("reuse-rules")).toBeNull();
  });

  it("turning it on sets reuseSandbox and nothing else", async () => {
    const onChange = vi.fn();
    renderWithProviders(<SandboxReuseCard value={trigger} onChange={onChange} />);

    await userEvent.click(screen.getByRole("switch", { name: "Reuse sandbox" }));

    expect(onChange).toHaveBeenCalledWith({ ...trigger, reuseSandbox: true });
  });

  it("states the rules a reused sandbox holds the function to once on", () => {
    renderWithProviders(
      <SandboxReuseCard value={{ ...trigger, reuseSandbox: true }} onChange={vi.fn()} />,
    );
    const rules = screen.getByTestId("reuse-rules").textContent ?? "";
    expect(rules).toMatch(/own input and caller/);
    expect(rules).toMatch(/module-level variables/);
    expect(rules).toMatch(/ctx\.waitUntil\(\)/);
    expect(rules).toMatch(/replaced/);
  });
});

import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { TriggerHttpCard } from "./trigger-http-card";
import { ITriggerConfig } from "../../types/function.types";

const tokenTrigger: ITriggerConfig = {
  httpEnabled: true,
  authMode: "Token",
  roles: ["finance-admin"],
  permissions: [],
  roleMatch: "Any",
  permissionMatch: "Any",
  workflowEnabled: true,
};

const render = (value: ITriggerConfig, onChange = vi.fn()) => {
  renderWithProviders(<TriggerHttpCard value={value} onChange={onChange} functionId="fn-1" />);
  return onChange;
};

describe("TriggerHttpCard", () => {
  it("presents HTTP as always on, with no enable switch", () => {
    render(tokenTrigger);
    expect(screen.getByText(/always on/i)).toBeTruthy();
    expect(screen.queryByRole("switch")).toBeNull();
  });

  it("switches to public and warns about the lost identity", async () => {
    const onChange = render(tokenTrigger);

    await userEvent.click(screen.getByRole("radio", { name: /^public/i }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ authMode: "Public" }));
  });

  it("warns when the trigger is already public", () => {
    render({ ...tokenTrigger, authMode: "Public" });
    expect(screen.getByText(/anonymous callers get no identity/i)).toBeTruthy();
  });

  it("applies one OR/AND choice to both roles and permissions", async () => {
    const onChange = render(tokenTrigger);

    await userEvent.click(screen.getByRole("radio", { name: "AND" }));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ roleMatch: "All", permissionMatch: "All" }),
    );
  });

  it("summarises what the restriction means", () => {
    render(tokenTrigger);
    expect(screen.getByText(/at least one of the listed roles or permissions/i)).toBeTruthy();
  });

  it("says so when nothing is restricted", () => {
    render({ ...tokenTrigger, roles: [] });
    expect(screen.getByText(/no extra restriction/i)).toBeTruthy();
  });
});

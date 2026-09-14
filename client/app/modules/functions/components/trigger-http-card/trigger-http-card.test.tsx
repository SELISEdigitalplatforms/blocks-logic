import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { TriggerHttpCard } from "./trigger-http-card";
import { ITriggerConfig } from "../../types/function.types";

vi.mock("@/modules/proxy/hooks", () => ({
  useIamRoles: () => ({
    data: [{ label: "Finance admin", value: "finance-admin" }],
    isLoading: false,
  }),
  useIamPermissions: () => ({ data: [], isLoading: false }),
}));

const tokenTrigger: ITriggerConfig = {
  httpEnabled: true,
  httpMethod: "Post",
  authMode: "Token",
  roles: ["finance-admin"],
  permissions: [],
  roleMatch: "Any",
  permissionMatch: "Any",
  combine: "Or",
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
    expect(screen.getByText(/\/logic\/v4\/fn\/fn-1/)).toBeTruthy();
  });

  it("offers GET and POST, one at a time, and shows only the chosen one on the endpoint", async () => {
    const onChange = render(tokenTrigger);

    expect(screen.getByRole("tab", { name: "GET" })).toBeTruthy();
    expect(screen.getByRole("tab", { name: "POST" })).toBeTruthy();
    expect(screen.queryByRole("tab", { name: /PUT|PATCH|DELETE/ })).toBeNull();
    // The badge on the endpoint block is the trigger's method — the tabs are the other POST.
    expect(screen.getAllByText("POST")).toHaveLength(2);
    expect(screen.getAllByText("GET")).toHaveLength(1);

    await userEvent.click(screen.getByRole("tab", { name: "GET" }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ httpMethod: "Get" }));
  });

  it("offers the proxy's two kinds, by name", () => {
    render(tokenTrigger);
    expect(screen.getByRole("radio", { name: "Blocks token" })).toBeTruthy();
    expect(screen.getByRole("radio", { name: "Public" })).toBeTruthy();
  });

  it("switching to public clears the rules, since they would no longer apply", async () => {
    const onChange = render(tokenTrigger);

    await userEvent.click(screen.getByRole("radio", { name: "Public" }));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ authMode: "Public", roles: [], permissions: [] }),
    );
  });

  it("says what public actually needs when the trigger is already public", () => {
    render({ ...tokenTrigger, authMode: "Public", roles: [] });
    expect(screen.getByTestId("access-summary").textContent).toMatch(/project key/);
    expect(screen.queryByTestId("access-restrictions")).toBeNull();
  });

  it("the OR/AND toggle sets the combine between the lists, and nothing else", async () => {
    const onChange = render(tokenTrigger);

    await userEvent.click(screen.getByRole("tab", { name: "AND" }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ combine: "And" }));
    const next = onChange.mock.calls[0][0] as ITriggerConfig;
    expect(next.roleMatch).toBe("Any");
    expect(next.permissionMatch).toBe("Any");
  });

  it("each list gets its own any/all once it holds more than one entry", async () => {
    const onChange = render({ ...tokenTrigger, roles: ["finance-admin", "auditor"] });

    await userEvent.click(screen.getByRole("button", { name: /caller needs any of these roles/i }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ roleMatch: "All" }));
  });

  it("shows the tenant's IAM label for a chosen role, and removes it", async () => {
    const onChange = render(tokenTrigger);
    expect(screen.getByText("Finance admin")).toBeTruthy();

    await userEvent.click(screen.getByRole("button", { name: /remove role finance admin/i }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ roles: [] }));
  });

  it("summarises the restriction the way the backend enforces it", () => {
    render({ ...tokenTrigger, permissions: ["orders.write"] });
    expect(screen.getByTestId("access-summary").textContent).toBe(
      "Callers must hold the role finance-admin OR the permission orders.write.",
    );
  });

  it("says so when nothing is restricted", () => {
    render({ ...tokenTrigger, roles: [] });
    expect(screen.getByText(/no extra restriction/i)).toBeTruthy();
  });
});

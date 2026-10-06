import { describe, expect, it, vi } from "vitest";
import { screen, within } from "@testing-library/react";
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
  httpMethods: [],
  reuseSandbox: false,
  responseMode: "async",
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
    // The badge on the endpoint block is the trigger's method — the tabs and the accepted-methods
    // toggles are the other two POSTs.
    expect(screen.getAllByText("POST")).toHaveLength(3);
    expect(screen.getAllByText("GET")).toHaveLength(2);

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
    expect(screen.getByTestId("access-summary").textContent).toMatch(/x-blocks-key/);
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

  it("shows the single legacy method as the only accepted one", () => {
    render(tokenTrigger);
    const group = screen.getByRole("group", { name: "Accepted methods" });
    expect(
      within(group).getByRole("button", { name: "Accept POST" }).getAttribute("aria-pressed"),
    ).toBe("true");
    expect(
      within(group).getByRole("button", { name: "Accept PUT" }).getAttribute("aria-pressed"),
    ).toBe("false");
  });

  it("adding a second verb turns the single method into a list", async () => {
    const onChange = render(tokenTrigger);

    await userEvent.click(screen.getByRole("button", { name: "Accept PUT" }));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ httpMethods: ["POST", "PUT"] }),
    );
  });

  it("going back to one GET or POST goes back to the legacy single method", async () => {
    const onChange = render({ ...tokenTrigger, httpMethods: ["GET", "POST"] });

    await userEvent.click(screen.getByRole("button", { name: "Accept POST" }));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ httpMethods: [], httpMethod: "Get" }),
    );
  });

  it("keeps a lone PUT as a list, since the single method cannot hold it", async () => {
    const onChange = render({ ...tokenTrigger, httpMethods: ["PUT", "DELETE"] });

    await userEvent.click(screen.getByRole("button", { name: "Accept DELETE" }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ httpMethods: ["PUT"] }));
  });

  it("never removes the last accepted verb", async () => {
    const onChange = render(tokenTrigger);

    await userEvent.click(screen.getByRole("button", { name: "Accept POST" }));

    expect(onChange).not.toHaveBeenCalled();
  });

  it("hides the single-method tabs while a multi-verb list is in use, so nothing can collapse it", async () => {
    const onChange = render({ ...tokenTrigger, httpMethods: ["GET", "PUT"] });

    expect(screen.queryByRole("tab", { name: "POST" })).toBeNull();
    // Tabbing through the card (focus alone) must never rewrite the method list.
    await userEvent.tab();
    await userEvent.tab();
    expect(onChange).not.toHaveBeenCalled();
  });

  it("marks the last accepted verb as not removable instead of silently ignoring the click", async () => {
    const onChange = render({ ...tokenTrigger, httpMethods: ["PUT"] });
    const put = screen.getByRole("button", { name: "Accept PUT" });

    expect(put.getAttribute("aria-disabled")).toBe("true");
    expect(put.getAttribute("title")).toBe("At least one method is required");
    await userEvent.click(put);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("shows every accepted verb on the endpoint", () => {
    render({ ...tokenTrigger, httpMethods: ["GET", "PATCH"] });
    expect(screen.getByText(/Any other method/)).toBeTruthy();
  });

  it("offers background and wait-for-answer responses, background by default", () => {
    render(tokenTrigger);
    const background = screen.getByRole("radio", { name: "Background (202 + poll)" });
    expect(background.getAttribute("aria-checked")).toBe("true");
    expect(screen.getByRole("radio", { name: "Wait for answer (API)" })).toBeTruthy();
  });

  it("choosing wait-for-answer sets the sync response mode", async () => {
    const onChange = render(tokenTrigger);

    await userEvent.click(screen.getByRole("radio", { name: "Wait for answer (API)" }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ responseMode: "sync" }));
  });

  it("never offers a cookie option: answers can not set cookies on the platform origin", () => {
    render({ ...tokenTrigger, responseMode: "sync" });
    expect(screen.queryByRole("switch", { name: "Allow cookies" })).toBeNull();
  });
});

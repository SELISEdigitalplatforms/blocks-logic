import { describe, expect, it, vi } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { TriggerHttpFields } from "./trigger-http-fields";
import { ITriggerConfig } from "../../types/function.types";

const ARIA_PRESSED = "aria-pressed";
const FINANCE_ADMIN = "finance-admin";
const ACCEPT_POST = "Accept POST";
const ACCEPT_PUT = "Accept PUT";
const ACCEPT_DELETE = "Accept DELETE";

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
  roles: [FINANCE_ADMIN],
  permissions: [],
  roleMatch: "Any",
  permissionMatch: "Any",
  combine: "Or",
  workflowEnabled: true,
};

const render = (value: ITriggerConfig, onChange = vi.fn()) => {
  renderWithProviders(<TriggerHttpFields value={value} onChange={onChange} functionId="fn-1" />);
  return onChange;
};

describe("TriggerHttpFields", () => {
  it("shows the address of the function and holds no on/off switch of its own", () => {
    render(tokenTrigger);
    expect(screen.getByText("Address")).toBeTruthy();
    expect(screen.queryByRole("switch")).toBeNull();
    expect(screen.getByText(/\/logic\/v4\/fn\/fn-1/)).toBeTruthy();
  });

  it("has one method editor only: the accepted-methods buttons, no separate GET/POST switch", async () => {
    const onChange = render(tokenTrigger);

    expect(screen.queryByRole("tablist", { name: "HTTP method" })).toBeNull();
    expect(screen.getByRole("button", { name: ACCEPT_POST }).getAttribute(ARIA_PRESSED)).toBe(
      "true",
    );

    // Adding GET then dropping POST is how a POST function becomes a GET one.
    await userEvent.click(screen.getByRole("button", { name: "Accept GET" }));
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ httpMethods: ["GET", "POST"] }),
    );
  });

  it("stores a lone GET in the old single-method shape", async () => {
    const onChange = render({ ...tokenTrigger, httpMethods: ["GET", "POST"] });

    await userEvent.click(screen.getByRole("button", { name: ACCEPT_POST }));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ httpMethods: [], httpMethod: "Get" }),
    );
  });

  it("offers Anyone and Signed-in users, and says what each means", () => {
    render(tokenTrigger);
    expect(screen.getByRole("tab", { name: "Signed-in users" }).getAttribute("aria-selected")).toBe(
      "true",
    );
    expect(screen.getByRole("tab", { name: "Anyone" })).toBeTruthy();
    expect(screen.getByTestId("access-summary").textContent).toMatch(/Only signed-in users/);
  });

  it("switching to public clears the rules, since they would no longer apply", async () => {
    const onChange = render(tokenTrigger);

    await userEvent.click(screen.getByRole("tab", { name: "Anyone" }));

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
    const onChange = render({ ...tokenTrigger, roles: [FINANCE_ADMIN, "auditor"] });

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
    expect(screen.getByTestId("restriction-summary").textContent).toBe(
      "Callers must hold the role finance-admin OR the permission orders.write.",
    );
  });

  it("says so when nothing is restricted", () => {
    render({ ...tokenTrigger, roles: [] });
    expect(screen.getByText("No limit yet — any signed-in user can call it.")).toBeTruthy();
  });

  it("shows the single legacy method as the only accepted one", () => {
    render(tokenTrigger);
    const group = screen.getByRole("group", { name: "Allowed request types" });
    expect(
      within(group).getByRole("button", { name: ACCEPT_POST }).getAttribute(ARIA_PRESSED),
    ).toBe("true");
    expect(within(group).getByRole("button", { name: ACCEPT_PUT }).getAttribute(ARIA_PRESSED)).toBe(
      "false",
    );
  });

  it("adding a second verb turns the single method into a list", async () => {
    const onChange = render(tokenTrigger);

    await userEvent.click(screen.getByRole("button", { name: ACCEPT_PUT }));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ httpMethods: ["POST", "PUT"] }),
    );
  });

  it("going back to one GET or POST goes back to the legacy single method", async () => {
    const onChange = render({ ...tokenTrigger, httpMethods: ["GET", "POST"] });

    await userEvent.click(screen.getByRole("button", { name: ACCEPT_POST }));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ httpMethods: [], httpMethod: "Get" }),
    );
  });

  it("keeps a lone PUT as a list, since the single method cannot hold it", async () => {
    const onChange = render({ ...tokenTrigger, httpMethods: ["PUT", "DELETE"] });

    await userEvent.click(screen.getByRole("button", { name: ACCEPT_DELETE }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ httpMethods: ["PUT"] }));
  });

  it("never removes the last accepted verb", async () => {
    const onChange = render(tokenTrigger);

    await userEvent.click(screen.getByRole("button", { name: ACCEPT_POST }));

    expect(onChange).not.toHaveBeenCalled();
  });

  it("marks the last accepted verb as not removable instead of silently ignoring the click", async () => {
    const onChange = render({ ...tokenTrigger, httpMethods: ["PUT"] });
    const put = screen.getByRole("button", { name: ACCEPT_PUT });

    expect(put.getAttribute("aria-disabled")).toBe("true");
    expect(put.getAttribute("title")).toBe("At least one method is required");
    await userEvent.click(put);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("shows every accepted verb on the endpoint", () => {
    render({ ...tokenTrigger, httpMethods: ["GET", "PATCH"] });
    expect(screen.getByText(/Any other method/)).toBeTruthy();
  });

  it("offers three plain responses, with a run ID and a later result as the default", () => {
    render(tokenTrigger);
    expect(
      screen.getByRole("tab", { name: "A run ID, result later" }).getAttribute("aria-selected"),
    ).toBe("true");
    expect(screen.getByRole("tab", { name: "The result" })).toBeTruthy();
    expect(screen.getByRole("tab", { name: "Live updates" })).toBeTruthy();
    expect(screen.getByTestId("response-help").textContent).toMatch(/checks for the result later/);
  });

  it("choosing The result sets the sync response mode", async () => {
    const onChange = render(tokenTrigger);

    await userEvent.click(screen.getByRole("tab", { name: "The result" }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ responseMode: "sync" }));
  });

  it("choosing Live updates sets the sse response mode and shows its help line", async () => {
    const onChange = render(tokenTrigger);

    await userEvent.click(screen.getByRole("tab", { name: "Live updates" }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ responseMode: "sse" }));

    render({ ...tokenTrigger, responseMode: "sse" });
    expect(screen.getAllByTestId("response-help").at(-1)?.textContent).toMatch(
      /message when the run starts/,
    );
  });

  it("never offers a cookie option: answers can not set cookies on the platform origin", () => {
    render({ ...tokenTrigger, responseMode: "sync" });
    expect(screen.queryByRole("switch", { name: "Allow cookies" })).toBeNull();
  });
});

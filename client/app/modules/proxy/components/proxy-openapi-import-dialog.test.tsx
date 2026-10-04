import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { ProxyOpenApiPreview, ProxyRoute } from "../types";
import { blankRoute } from "./proxy-routes-card";
import {
  ProxyOpenApiImportDialog,
  ProxyOpenApiImportResult,
} from "./proxy-openapi-import-dialog";

const previewOpenApi = vi.fn();

vi.mock("../services", () => ({
  proxyService: {
    previewOpenApi: (...args: unknown[]) => previewOpenApi(...args),
  },
}));

const preview = (overrides: Partial<ProxyOpenApiPreview> = {}): ProxyOpenApiPreview => ({
  baseUrl: "https://api.vendor.com/v1",
  operations: [
    {
      operationId: "getOrder",
      method: "GET",
      path: "orders/{id}",
      summary: "Fetch one order",
      queryParameters: ["expand"],
      headerParameters: ["X-Trace"],
      securityHeaders: ["X-Api-Key"],
      alreadyExists: false,
    },
    {
      operationId: "deleteOrder",
      method: "DELETE",
      path: "orders/{id}",
      summary: "",
      queryParameters: [],
      headerParameters: [],
      securityHeaders: ["X-Api-Key"],
      alreadyExists: false,
    },
  ],
  errors: [],
  warnings: [],
  ...overrides,
});

const open = async (
  props: Partial<React.ComponentProps<typeof ProxyOpenApiImportDialog>> = {},
) => {
  const onImport = vi.fn();
  const user = userEvent.setup();
  renderWithProviders(
    <ProxyOpenApiImportDialog
      open
      onOpenChange={vi.fn()}
      existingRoutes={[]}
      existingCredentialKeys={[]}
      upstreamUrl=""
      onImport={onImport}
      {...props}
    />,
  );
  return { user, onImport };
};

const read = async (user: ReturnType<typeof userEvent.setup>, spec = '{"openapi":"3.0.0"}') => {
  // `fireEvent`, not `user.type`: braces are keyboard syntax to userEvent, and a specification is
  // mostly braces.
  fireEvent.change(screen.getByLabelText("OpenAPI 3 document (JSON)"), { target: { value: spec } });
  await user.click(screen.getByRole("button", { name: "Read document" }));
};

const result = (onImport: ReturnType<typeof vi.fn>): ProxyOpenApiImportResult =>
  onImport.mock.calls[0][0];

describe("ProxyOpenApiImportDialog", () => {
  beforeEach(() => {
    previewOpenApi.mockReset();
    previewOpenApi.mockResolvedValue(preview());
  });

  it("proposes every operation the document describes, already ticked", async () => {
    const { user } = await open();

    await read(user);

    await waitFor(() => expect(screen.getByText(/2 of 2 operations can be added/)).toBeTruthy());
    expect(
      (screen.getByLabelText("Import GET orders/{id}") as HTMLInputElement).getAttribute(
        "aria-checked",
      ),
    ).toBe("true");
  });

  it("an endpoint the form already has is shown, disabled, and never imported", async () => {
    // Importing over it would discard what the document knows nothing about — the injected
    // credential, the response projection, the timeout somebody chose.
    const existing: ProxyRoute[] = [{ ...blankRoute("GET"), path: "orders/{id}" }];
    const { user, onImport } = await open({ existingRoutes: existing });

    await read(user);

    await waitFor(() => expect(screen.getByText(/1 of 2 operations can be added/)).toBeTruthy());
    expect(screen.getByText("already defined — kept as it is")).toBeTruthy();
    expect(
      (screen.getByLabelText("Import GET orders/{id}") as HTMLInputElement).hasAttribute("disabled"),
    ).toBe(true);

    await user.click(screen.getByRole("button", { name: "Add 1 endpoint" }));
    expect(result(onImport).routes.map((route) => route.method)).toEqual(["DELETE"]);
  });

  it("collisions are judged against the form, not only against what was saved", async () => {
    // The form replaces the whole route list on save, so a row added a minute ago collides just as
    // surely as one the server already knows about — and the server was never told about it.
    const existing: ProxyRoute[] = [{ ...blankRoute("DELETE"), path: "orders/{id}" }];
    const { user } = await open({ existingRoutes: existing });

    await read(user);

    await waitFor(() => expect(screen.getByText(/1 of 2 operations can be added/)).toBeTruthy());
  });

  it("imported rows carry parameter names and never a value", async () => {
    const { user, onImport } = await open();

    await read(user);
    await waitFor(() => screen.getByRole("button", { name: "Add 2 endpoints" }));
    await user.click(screen.getByRole("button", { name: "Add 2 endpoints" }));

    const route = result(onImport).routes[0];
    expect(route).toMatchObject({
      method: "GET",
      path: "orders/{id}",
      // The vendor's own shape to begin with, so the client-facing path can be renamed later.
      upstreamPath: "orders/{id}",
      resilience: null,
    });
    expect(route.query).toEqual([{ key: "expand", value: "" }]);
    expect(route.headers).toEqual([{ key: "X-Trace", value: "" }]);
  });

  it("a security scheme becomes a connection credential with no value", async () => {
    // One home for the credential, as the rest of the form teaches — not a copy on every row.
    const { user, onImport } = await open();

    await read(user);
    await waitFor(() => screen.getByRole("button", { name: "Add 2 endpoints" }));

    expect(screen.getByText(/set each one to a/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Add 2 endpoints" }));

    expect(result(onImport).credentialKeys).toEqual(["X-Api-Key"]);
  });

  it("a credential the connection already sends is not proposed again", async () => {
    const { user, onImport } = await open({ existingCredentialKeys: ["x-api-key"] });

    await read(user);
    await waitFor(() => screen.getByRole("button", { name: "Add 2 endpoints" }));
    await user.click(screen.getByRole("button", { name: "Add 2 endpoints" }));

    expect(result(onImport).credentialKeys).toEqual([]);
  });

  it("offers the document's server when the connection has no vendor URL yet", async () => {
    const { user, onImport } = await open();

    await read(user);
    await waitFor(() => screen.getByRole("button", { name: "Add 2 endpoints" }));
    expect(screen.getByText("The connection has no vendor URL yet.")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Add 2 endpoints" }));
    expect(result(onImport).upstreamUrl).toBe("https://api.vendor.com/v1");
  });

  it("never replaces a vendor URL the user already typed without being asked", async () => {
    const { user, onImport } = await open({ upstreamUrl: "https://api.other.com" });

    await read(user);
    await waitFor(() => screen.getByRole("button", { name: "Add 2 endpoints" }));
    expect(screen.getByText(/points somewhere else/)).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Add 2 endpoints" }));
    expect(result(onImport).upstreamUrl).toBeNull();
  });

  it("a document that could not be read says why, and proposes nothing", async () => {
    previewOpenApi.mockResolvedValue({
      baseUrl: "",
      operations: [],
      errors: ["That is valid JSON but not an OpenAPI document."],
      warnings: [],
    });
    const { user } = await open();

    await read(user, "{}");

    await waitFor(() =>
      expect(screen.getByText("That is valid JSON but not an OpenAPI document.")).toBeTruthy(),
    );
    expect(screen.getByRole("button", { name: "Add endpoints" }).hasAttribute("disabled")).toBe(
      true,
    );
  });

  it("says what it skipped rather than quietly dropping it", async () => {
    previewOpenApi.mockResolvedValue(
      preview({ warnings: ["TRACE /health is not a method this gateway can forward."] }),
    );
    const { user } = await open();

    await read(user);

    await waitFor(() =>
      expect(
        screen.getByText("TRACE /health is not a method this gateway can forward."),
      ).toBeTruthy(),
    );
  });

  it("sends the URL to the server rather than fetching it from the browser", async () => {
    // The server is where the address checks live. A browser fetch would also be a cross-origin
    // request the vendor has no reason to allow.
    const { user } = await open();

    await user.click(screen.getByRole("button", { name: "Fetch from a URL" }));
    await user.type(screen.getByLabelText("Document URL"), "https://api.vendor.com/openapi.json");
    await user.click(screen.getByRole("button", { name: "Read document" }));

    await waitFor(() =>
      expect(previewOpenApi).toHaveBeenCalledWith({
        specUrl: "https://api.vendor.com/openapi.json",
      }),
    );
  });
});

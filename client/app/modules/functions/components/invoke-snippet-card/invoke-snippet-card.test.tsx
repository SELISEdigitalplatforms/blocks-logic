import { afterEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { InvokeSnippetCard } from "./invoke-snippet-card";
import { snippetBlocksKey } from "../../constants/endpoint.constant";
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

const setTenant = (tenantId: string) =>
  useProjectStore.setState({ selectedProject: { tenantId, tenantSlug: "", projectKey: "" } });

const snippetText = () => screen.getByText(/curl/).textContent ?? "";

describe("InvokeSnippetCard", () => {
  afterEach(() => setTenant(""));

  it("fills x-blocks-key with the selected environment's tenant id", () => {
    setTenant("A1B2C3D4E5F6");
    renderWithProviders(<InvokeSnippetCard functionId="fn-1" trigger={trigger} />);
    expect(snippetText()).toContain('-H "x-blocks-key: A1B2C3D4E5F6"');
    expect(snippetText()).not.toContain("<your x-blocks-key>");
  });

  it("fills the key on a GET trigger too", () => {
    setTenant("A1B2C3D4E5F6");
    renderWithProviders(
      <InvokeSnippetCard functionId="fn-1" trigger={{ ...trigger, httpMethod: "Get" }} />,
    );
    expect(snippetText()).toContain('-H "x-blocks-key: A1B2C3D4E5F6"');
  });

  it("uses an accepted verb other than GET or POST with a body", () => {
    renderWithProviders(
      <InvokeSnippetCard
        functionId="fn-1"
        trigger={{ ...trigger, httpMethods: ["PUT", "DELETE"] }}
      />,
    );
    expect(snippetText()).toContain("curl -X PUT");
    expect(screen.getByText(/"method": "PUT"/)).toBeTruthy();
  });

  it("shows the function's own answer in sync mode, with the 202 fallback", () => {
    renderWithProviders(
      <InvokeSnippetCard functionId="fn-1" trigger={{ ...trigger, responseMode: "sync" }} />,
    );
    expect(screen.getByText(/200 OK/)).toBeTruthy();
    expect(screen.getByText(/statusCode, headers, body/)).toBeTruthy();
    expect(screen.queryByText(/202 Accepted/)).toBeNull();
  });

  it("keeps the placeholder when no environment is selected", () => {
    useProjectStore.setState({ selectedProject: null });
    renderWithProviders(<InvokeSnippetCard functionId="fn-1" trigger={trigger} />);
    expect(snippetText()).toContain('-H "x-blocks-key: <your x-blocks-key>"');
  });
});

describe("snippetBlocksKey", () => {
  it.each([undefined, null, "", "   "])("falls back to the placeholder for %j", (value) => {
    expect(snippetBlocksKey(value)).toBe("<your x-blocks-key>");
  });

  it("trims surrounding whitespace", () => {
    expect(snippetBlocksKey("  abc-123_X  ")).toBe("abc-123_X");
  });

  it.each(['ab"c', "a b", "a$(id)", "a\\nb", "a'b"])(
    "refuses a value that would break the quoted header: %j",
    (value) => {
      expect(snippetBlocksKey(value)).toBe("<your x-blocks-key>");
    },
  );
});

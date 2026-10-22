import { afterEach, describe, expect, it } from "vitest";
import { fireEvent, screen } from "@testing-library/react";
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

const snippetText = () => screen.getByTestId("invoke-snippet").textContent ?? "";
const pollText = () => screen.getByTestId("poll-snippet").textContent ?? "";
const setDomain = (customDomain: string) =>
  useProjectStore.setState({
    selectedProject: { tenantId: "T1", tenantSlug: "", projectKey: "", customDomain },
  });

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

describe("InvokeSnippetCard base URL, SSE and polling", () => {
  afterEach(() => setTenant(""));

  it("builds the base URL from the custom domain's root", () => {
    setDomain("app.acme.com");
    renderWithProviders(<InvokeSnippetCard functionId="fn-1" trigger={trigger} />);
    expect(snippetText()).toMatch(/^BASE_URL="https:\/\/blocksapi\.acme\.com"\n\ncurl /);
    expect(snippetText()).toContain('"$BASE_URL/logic/v4/fn/fn-1/orders/42?notify=true"');
    expect(screen.queryByText(/Add a custom domain/)).toBeNull();
  });

  it("shows a placeholder and a hint without a custom domain", () => {
    setTenant("T1");
    renderWithProviders(<InvokeSnippetCard functionId="fn-1" trigger={trigger} />);
    expect(snippetText()).toContain('BASE_URL="https://blocksapi.<your-domain>"');
    expect(screen.getByText(/Add a custom domain to the project/)).toBeTruthy();
  });

  it("on a sync function, SSE mode adds -N and the Accept header and shows the two events", () => {
    setDomain("acme.com");
    renderWithProviders(
      <InvokeSnippetCard functionId="fn-1" trigger={{ ...trigger, responseMode: "sync" }} />,
    );
    expect(snippetText()).not.toContain("-N");
    fireEvent.click(screen.getByRole("button", { name: "SSE" }));
    expect(snippetText()).toContain("curl -N -X POST");
    expect(snippetText()).toContain('-H "Accept: text/event-stream"');
    const response = screen.getByText(/200 OK · text\/event-stream/).textContent ?? "";
    // Exactly two events: the run id first, then the full response.
    expect(response.match(/^event: /gm)).toEqual(["event: ", "event: "]);
    expect(response).toMatch(/event: run\ndata: \{ "runId": "run_7f21c4" \}\n\nevent: done\n/);
    expect(response).toContain('"result": { "ok": true }');
  });

  it("on an SSE function, always shows the event stream, with no toggle and no Accept header", () => {
    setDomain("acme.com");
    renderWithProviders(
      <InvokeSnippetCard functionId="fn-1" trigger={{ ...trigger, responseMode: "sse" }} />,
    );
    expect(screen.queryByRole("button", { name: "SSE" })).toBeNull();
    expect(snippetText()).toContain("curl -N -X POST");
    expect(snippetText()).not.toContain("Accept: text/event-stream");
    expect(screen.getByText(/200 OK · text\/event-stream/)).toBeTruthy();
    expect(screen.getByText(/Only needed when the last event is STILL_RUNNING/)).toBeTruthy();
  });

  it("on an async function, offers no SSE and shows the 202", () => {
    renderWithProviders(<InvokeSnippetCard functionId="fn-1" trigger={trigger} />);
    expect(screen.queryByRole("button", { name: "SSE" })).toBeNull();
    expect(snippetText()).not.toContain("-N");
    expect(screen.getByText(/202 Accepted/)).toBeTruthy();
  });

  it("shows the poll curl with the poll token header in every mode", () => {
    setDomain("acme.com");
    renderWithProviders(
      <InvokeSnippetCard functionId="fn-1" trigger={{ ...trigger, responseMode: "sync" }} />,
    );
    expect(pollText()).toContain('curl "$BASE_URL/logic/v4/fn/runs/<runId>/result"');
    expect(pollText()).toContain('-H "x-blocks-key: T1"');
    expect(pollText()).toContain('-H "x-poll-token: <pollToken>"');
    fireEvent.click(screen.getByRole("button", { name: "SSE" }));
    expect(pollText()).toContain("x-poll-token");
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

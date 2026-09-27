import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
vi.mock("../services", async () => ({
  proxyService: (await import("../test-support/mock-proxy-service")).mockProxyService,
}));

import { proxyService } from "../services";
import { PROXY_MOCK_DATA } from "../constants";
import { Proxy, ProxyRoute } from "../types";
import { ProxyTestTab } from "./proxy-test-tab";

const testSpy = vi.spyOn(proxyService, "test");

const stripe = () => ({ ...(PROXY_MOCK_DATA[0] as Proxy) });

const route = (overrides: Partial<ProxyRoute>): ProxyRoute => ({
  method: "GET",
  path: "",
  upstreamPath: null,
  headers: null,
  query: null,
  bodyMerge: null,
  responseMode: null,
  responseInclude: null,
  ...overrides,
});

const queryInput = () => screen.getByLabelText("Query string") as HTMLInputElement;
const bodyInput = () => screen.getByLabelText(/request body/i) as HTMLTextAreaElement;

const pickMethod = async (user: ReturnType<typeof userEvent.setup>, method: string) => {
  await user.click(screen.getByLabelText("Test method"));
  await user.click(await screen.findByRole("option", { name: method }));
};

const pickRoute = async (user: ReturnType<typeof userEvent.setup>, name: string) => {
  await user.click(screen.getByLabelText("Test route"));
  await user.click(await screen.findByRole("option", { name }));
};

describe("ProxyTestTab", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("sends the selected route as a path suffix and renders the result", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProxyTestTab proxy={stripe()} />);

    // Prefilled with the configured query; the typed param is appended to it.
    await user.type(screen.getByLabelText("Query string"), "&limit=10");
    await user.click(screen.getByRole("button", { name: /send test request/i }));

    await waitFor(() =>
      expect(testSpy).toHaveBeenCalledWith(
        expect.objectContaining({
          proxyId: "p1",
          method: "GET",
          pathSuffix: "",
          query: "expand[]=payment_intent&limit=10",
        }),
      ),
    );
    expect(await screen.findByText("200")).toBeTruthy();
    expect(screen.getByText("OK")).toBeTruthy();
  });

  it("requires a route parameter before it will send", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProxyTestTab proxy={stripe()} />);

    await user.click(screen.getByLabelText("Test route"));
    await user.click(await screen.findByRole("option", { name: "/{id}" }));

    const send = screen.getByRole("button", { name: /send test request/i });
    expect(send.hasAttribute("disabled")).toBe(true);
    expect(screen.getByText("Fill in {id} to send.")).toBeTruthy();

    await user.type(screen.getByLabelText("{id}"), "ch_123");
    await user.click(send);

    await waitFor(() =>
      expect(testSpy).toHaveBeenCalledWith(expect.objectContaining({ pathSuffix: "ch_123" })),
    );
  });

  it("offers a body only on body-carrying methods", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProxyTestTab proxy={stripe()} />);

    expect(screen.queryByLabelText(/request body/i)).toBeNull();

    await user.click(screen.getByLabelText("Test method"));
    await user.click(await screen.findByRole("option", { name: "POST" }));

    expect(screen.getByLabelText(/request body/i)).toBeTruthy();
  });

  it("refuses to send while the proxy is paused", () => {
    renderWithProviders(<ProxyTestTab proxy={{ ...stripe(), enabled: false }} />);

    expect(
      screen.getByRole("button", { name: /send test request/i }).hasAttribute("disabled"),
    ).toBe(true);
    expect(screen.getByText(/paused/i)).toBeTruthy();
  });
  describe("prefill from config", () => {
    it("prefills the query and lists the headers the proxy adds on mount", () => {
      renderWithProviders(<ProxyTestTab proxy={stripe()} />);

      expect(queryInput().value).toBe("expand[]=payment_intent");
      const headers = screen.getByRole("list", { name: "Headers the proxy adds" });
      expect(within(headers).getByText("Authorization")).toBeTruthy();
      expect(within(headers).getByText("{{$VAR.stripe-api-key}}")).toBeTruthy();
      expect(within(headers).getByText("connection")).toBeTruthy();
    });

    it("keeps variable tokens un-encoded in the prefilled query", () => {
      renderWithProviders(
        <ProxyTestTab
          proxy={{ ...stripe(), query: [{ key: "key", value: "{{$VAR.weather-api-key}}" }] }}
        />,
      );

      expect(queryInput().value).toBe("key={{$VAR.weather-api-key}}");
    });

    it("re-prefills when the route changes, but not on a re-render", async () => {
      const user = userEvent.setup();
      const proxy: Proxy = {
        ...stripe(),
        routes: [
          route({ path: "" }),
          route({ path: "orders", query: [{ key: "status", value: "open" }] }),
        ],
      };
      const { rerender } = renderWithProviders(<ProxyTestTab proxy={proxy} />);

      fireEvent.change(queryInput(), { target: { value: "typed=1" } });
      rerender(<ProxyTestTab proxy={{ ...proxy }} />);
      expect(queryInput().value).toBe("typed=1");

      await pickRoute(user, "/orders");
      expect(queryInput().value).toBe("expand[]=payment_intent&status=open");

      fireEvent.change(queryInput(), { target: { value: "" } });
      await user.click(screen.getByRole("button", { name: "Reset query string to config" }));
      expect(queryInput().value).toBe("expand[]=payment_intent&status=open");
    });

    it("notes configured query keys whose typed value is ignored", () => {
      renderWithProviders(<ProxyTestTab proxy={stripe()} />);

      const note = () =>
        screen.queryByText(
          (_, element) =>
            element?.tagName === "P" &&
            /^expand\[\] is set by the proxy/.test(element.textContent ?? ""),
        );
      expect(note()).toBeTruthy();

      fireEvent.change(queryInput(), { target: { value: "limit=10" } });
      expect(note()).toBeNull();

      // Typed by hand, a configured key is still called out.
      fireEvent.change(queryInput(), { target: { value: "limit=10&expand%5B%5D=other" } });
      expect(note()).toBeTruthy();
    });

    it("prefills the body with the endpoint's merged fields on body methods only", async () => {
      const user = userEvent.setup();
      renderWithProviders(<ProxyTestTab proxy={stripe()} />);

      expect(screen.queryByLabelText(/request body/i)).toBeNull();
      await pickMethod(user, "POST");

      expect(JSON.parse(bodyInput().value)).toEqual({
        account: "acct_platform",
        idempotency_key: "{{$VAR.stripe-idempotency}}",
      });
      expect(screen.getByText(/2 configured fields are merged/)).toBeTruthy();
      expect(
        screen.getByText(
          (_, element) =>
            element?.tagName === "P" &&
            /^account, idempotency_key are set by the proxy/.test(element.textContent ?? ""),
        ),
      ).toBeTruthy();
      // The POST method override's header joins the connection's.
      const headers = screen.getByRole("list", { name: "Headers the proxy adds" });
      expect(within(headers).getByText("X-Trace")).toBeTruthy();
      expect(within(headers).getByText("method")).toBeTruthy();
    });

    it("uses the endpoint's own body fields, including an explicit none", async () => {
      const user = userEvent.setup();
      renderWithProviders(
        <ProxyTestTab
          proxy={{ ...stripe(), routes: [route({ method: "POST", bodyMerge: [] })] }}
        />,
      );

      await pickMethod(user, "POST");
      expect(bodyInput().value).toBe("");
      expect(screen.queryByText(/configured fields? (is|are) merged/)).toBeNull();
    });
  });
});

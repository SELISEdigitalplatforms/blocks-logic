import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
vi.mock("../services", async () => ({
  proxyService: (await import("../test-support/mock-proxy-service")).mockProxyService,
}));

import { proxyService } from "../services";
import { PROXY_MOCK_DATA } from "../constants";
import { Proxy } from "../types";
import { ProxyTestTab } from "./proxy-test-tab";

const testSpy = vi.spyOn(proxyService, "test");

const stripe = () => ({ ...(PROXY_MOCK_DATA[0] as Proxy) });

describe("ProxyTestTab", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("sends the selected route as a path suffix and renders the result", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProxyTestTab proxy={stripe()} />);

    await user.type(screen.getByLabelText("Query string"), "limit=10");
    await user.click(screen.getByRole("button", { name: /send test request/i }));

    await waitFor(() =>
      expect(testSpy).toHaveBeenCalledWith(
        expect.objectContaining({
          proxyId: "p1",
          method: "GET",
          pathSuffix: "",
          query: "limit=10",
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
});

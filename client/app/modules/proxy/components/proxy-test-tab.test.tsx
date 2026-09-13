import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { PROXY_MOCK_DATA } from "../constants";

vi.mock("../services", async () => ({
  proxyService: (await import("../test-support/mock-proxy-service")).mockProxyService,
}));

import { proxyService } from "../services";
import { ProxyTestTab } from "./proxy-test-tab";

const mockProxyService = proxyService as unknown as {
  resetMockStore: () => void;
  test: (request: unknown) => Promise<unknown>;
};

describe("ProxyTestTab", () => {
  beforeEach(() => {
    mockProxyService.resetMockStore();
    vi.clearAllMocks();
  });

  it("tests the saved proxy with the picked method and shows the response", async () => {
    const user = userEvent.setup();
    const testSpy = vi.spyOn(mockProxyService, "test");

    renderWithProviders(<ProxyTestTab proxy={PROXY_MOCK_DATA[0]} />);

    expect(screen.getByText("/api/proxy/gateway/stripe-payments")).toBeTruthy();
    await user.clear(screen.getByLabelText("Test request path"));
    await user.type(screen.getByLabelText("Test request path"), "/charges");
    await user.click(screen.getByRole("button", { name: /test run/i }));

    await waitFor(() =>
      expect(testSpy).toHaveBeenCalledWith(
        expect.objectContaining({ proxyId: "p1", method: "GET", pathSuffix: "/charges" }),
      ),
    );
    expect(await screen.findByText("200 OK")).toBeTruthy();
  });

  it("offers only the proxy's own methods and hides the body for GET", async () => {
    const user = userEvent.setup();

    renderWithProviders(<ProxyTestTab proxy={PROXY_MOCK_DATA[0]} />);

    // p1 allows GET and POST — the server rejects anything else.
    expect(screen.queryByLabelText("Test request body")).toBeNull();
    await user.click(screen.getByRole("combobox", { name: "Method" }));
    const options = await screen.findAllByRole("option");
    expect(options.map((option) => option.textContent)).toEqual(["GET", "POST"]);

    await user.click(screen.getByRole("option", { name: "POST" }));
    expect(await screen.findByLabelText("Test request body")).toBeTruthy();
  });

  it("renders a single-method proxy as a badge instead of a picker", async () => {
    renderWithProviders(<ProxyTestTab proxy={PROXY_MOCK_DATA[2]} />);

    expect(screen.queryByRole("combobox", { name: "Method" })).toBeNull();
    expect(screen.getByText("GET")).toBeTruthy();
  });
});

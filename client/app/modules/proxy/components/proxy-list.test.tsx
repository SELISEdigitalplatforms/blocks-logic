import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { PROXY_MOCK_DATA } from "../constants";

vi.mock("../services", async () => ({
  proxyService: (await import("../test-support/mock-proxy-service")).mockProxyService,
}));

import { proxyService } from "../services";
import { ProxyList } from "./proxy-list";

const mockProxyService = proxyService as unknown as { resetMockStore: () => void };

const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
}));

vi.mock("@/hooks/use-toast", () => toasts);

vi.mock("@seliseblocks/genesis-os", async () => {
  const actual = await vi.importActual("@seliseblocks/genesis-os");
  return {
    ...actual,
    useScopedPath: () => (path: string) => `/app/item-123/${path.replace(/^\//, "")}`,
  };
});

describe("ProxyList", () => {
  beforeEach(() => {
    mockProxyService.resetMockStore();
    vi.clearAllMocks();
  });

  it("shows loading and empty states", () => {
    const { rerender } = renderWithProviders(
      <MemoryRouter>
        <ProxyList proxies={[]} isLoading={true} />
      </MemoryRouter>,
    );

    expect(screen.queryByText("Loading proxies...")).toBeNull();
    expect(screen.queryByText("No proxies yet")).toBeNull();

    rerender(
      <MemoryRouter>
        <ProxyList proxies={[]} isLoading={false} />
      </MemoryRouter>,
    );
    expect(screen.getByText("No proxies yet")).toBeTruthy();
  });

  it("navigates to detail", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <MemoryRouter initialEntries={["/proxy"]}>
        <Routes>
          <Route
            path="/proxy"
            element={<ProxyList proxies={PROXY_MOCK_DATA} isLoading={false} />}
          />
          <Route path="/app/item-123/proxy/p1" element={<div>Stripe detail</div>} />
        </Routes>
      </MemoryRouter>,
    );

    await user.click(screen.getByText("Stripe Payments"));
    expect(screen.getByText("Stripe detail")).toBeTruthy();
  });

  it("toggles proxy status", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <MemoryRouter>
        <ProxyList proxies={PROXY_MOCK_DATA} isLoading={false} />
      </MemoryRouter>,
    );

    await user.click(screen.getByLabelText("Weather Lookup enabled"));
    await waitFor(() =>
      expect(toasts.showSuccessToast).toHaveBeenCalledWith({ description: "Proxy enabled." }),
    );
  });

  it("enables a proxy from the options menu", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <MemoryRouter>
        <ProxyList proxies={PROXY_MOCK_DATA} isLoading={false} />
      </MemoryRouter>,
    );

    await user.click(screen.getByRole("button", { name: "Weather Lookup options" }));
    await user.click(await screen.findByRole("menuitem", { name: "Enable" }));

    await waitFor(() =>
      expect(toasts.showSuccessToast).toHaveBeenCalledWith({ description: "Proxy enabled." }),
    );
  });

  it("deletes a proxy through the confirmation modal", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <MemoryRouter>
        <ProxyList proxies={PROXY_MOCK_DATA} isLoading={false} />
      </MemoryRouter>,
    );

    await user.click(screen.getByRole("button", { name: "Stripe Payments options" }));
    await user.click(await screen.findByRole("menuitem", { name: "Delete" }));

    expect(await screen.findByRole("heading", { name: "Delete Proxy" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Delete" }));

    await waitFor(() =>
      expect(toasts.showSuccessToast).toHaveBeenCalledWith({
        description: "Proxy deleted successfully.",
      }),
    );
  });
});

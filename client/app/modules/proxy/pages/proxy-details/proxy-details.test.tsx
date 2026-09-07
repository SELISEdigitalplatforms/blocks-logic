import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { proxyService } from "../../services";
import { ProxyDetails } from "./proxy-details";

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

describe("ProxyDetails page", () => {
  beforeEach(() => {
    proxyService.resetMockStore();
    vi.clearAllMocks();
  });

  it("renders overview tabs for a known proxy", async () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/proxy/p1"]}>
        <Routes>
          <Route path="/proxy/:proxyId" element={<ProxyDetails />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByRole("heading", { name: "Stripe Payments" })).toBeTruthy();
    expect(screen.getByRole("tab", { name: "Overview" })).toBeTruthy();
    expect(screen.getByRole("tab", { name: /Request logs/i })).toBeTruthy();
    expect(screen.getByRole("tab", { name: /Change history/i })).toBeTruthy();
  });

  it("redirects unknown proxies back to the list", async () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/proxy/missing"]}>
        <Routes>
          <Route path="/proxy/:proxyId" element={<ProxyDetails />} />
          <Route path="/app/item-123/proxy" element={<div>Proxy list route</div>} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() =>
      expect(toasts.showErrorToast).toHaveBeenCalledWith({ errors: "Proxy not found" }),
    );
    expect(await screen.findByText("Proxy list route")).toBeTruthy();
  });
});

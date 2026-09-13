import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Proxies } from "./pages/proxies";
import { ProxyDetails } from "./pages/proxy-details";
import { ProxyForm } from "./components/proxy-form";
import { ProxyFormPage } from "./pages/proxy-form-page";

vi.mock("./services", async () => ({
  proxyService: (await import("./test-support/mock-proxy-service")).mockProxyService,
}));

import { proxyService } from "./services";

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

describe("Proxy feature", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockProxyService.resetMockStore();
  });

  it("renders the mock-backed list with the expected summaries", async () => {
    renderWithProviders(
      <MemoryRouter>
        <Proxies />
      </MemoryRouter>,
    );

    expect(await screen.findByText("Stripe Payments")).toBeTruthy();
    expect(screen.getByText(/Your client calls Blocks/i)).toBeTruthy();
    expect(screen.getByText("SendGrid Mail")).toBeTruthy();
    expect(screen.getByText("Weather Lookup")).toBeTruthy();
    expect(screen.getByText(/1,248 calls 24h/i)).toBeTruthy();
  });

  it("renders detail overview and reveals the upstream endpoint", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <MemoryRouter initialEntries={["/proxy/p1"]}>
        <Routes>
          <Route path="/proxy/:proxyId" element={<ProxyDetails />} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Stripe Payments" })).toBeTruthy(),
    );

    expect(screen.getByText("Live")).toBeTruthy();
    // No trailing "/*": the gateway forwards only the endpoints the proxy declares, so advertising
    // a wildcard would promise paths that are refused.
    // The base URL appears once in the summary card and once per base-path endpoint (p1 has two).
    expect((await screen.findAllByText("/logic/v4/proxy/gateway/stripe-payments")).length).toBeGreaterThan(0);
    expect(screen.getByText("Authorization")).toBeTruthy();
    expect(screen.getAllByText("variable").length).toBeGreaterThan(0);

    // The upstream endpoint is the only masked value on the page; header/query values render as-is.
    await user.click(screen.getByRole("button", { name: /reveal third party endpoint/i }));
    expect(screen.getAllByText("https://api.stripe.com/v1/charges").length).toBeGreaterThan(0);
  });

  it("blocks create when name is blank and keeps the mock store unchanged", async () => {
    const user = userEvent.setup();
    const createSpy = vi.spyOn(proxyService, "create");
    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="create" />
      </MemoryRouter>,
    );

    await user.type(
      screen.getByPlaceholderText("https://api.vendor.com"),
      "https://api.stripe.com/v1/charges",
    );
    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(await screen.findByText("Give the proxy a name - it becomes the path.")).toBeTruthy();
    expect(createSpy).not.toHaveBeenCalled();
  });

  it("shows a not-found toast and returns to the proxy list for unknown ids", async () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/proxy/not-real/edit"]}>
        <Routes>
          <Route path="/proxy/:proxyId/edit" element={<ProxyFormPage mode="edit" />} />
          <Route path="/app/item-123/proxy" element={<div>Proxy list route</div>} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() =>
      expect(toasts.showErrorToast).toHaveBeenCalledWith({ errors: "Proxy not found" }),
    );
    expect(screen.getByText("Proxy list route")).toBeTruthy();
  });

  it("filters and expands request logs from the detail tab", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <MemoryRouter initialEntries={["/proxy/p1"]}>
        <Routes>
          <Route path="/proxy/:proxyId" element={<ProxyDetails />} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Stripe Payments" })).toBeTruthy(),
    );
    await user.click(screen.getByRole("tab", { name: /request logs/i }));

    expect(await screen.findByText("3 of 3 requests")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "4xx" }));
    expect(await screen.findByText("1 of 3 requests")).toBeTruthy();
    await user.click(screen.getByText("/api/proxy/gateway/stripe-payments/charges/ch_404"));
    expect(await screen.findByText("https://api.stripe.com/v1/charges/ch_404")).toBeTruthy();
    expect(screen.getByText(/Authorization/i)).toBeTruthy();
  });

  it("renders change history and reverts a version", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <MemoryRouter initialEntries={["/proxy/p1"]}>
        <Routes>
          <Route path="/proxy/:proxyId" element={<ProxyDetails />} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Stripe Payments" })).toBeTruthy(),
    );
    await user.click(screen.getByRole("tab", { name: /change history/i }));

    expect(await screen.findByText("Added payment intent expansion query")).toBeTruthy();
    await user.click(screen.getAllByRole("button", { name: /revert/i })[0]);
    await waitFor(() =>
      expect(toasts.showSuccessToast).toHaveBeenCalledWith({
        description: "Proxy version reverted.",
      }),
    );
  });
});

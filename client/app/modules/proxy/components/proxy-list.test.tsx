import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
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

  it("summarizes extra methods when a proxy has more than two", () => {
    renderWithProviders(
      <MemoryRouter>
        <ProxyList
          proxies={[
            {
              ...PROXY_MOCK_DATA[0],
              methods: ["GET", "POST", "PUT", "PATCH"],
            },
          ]}
          isLoading={false}
        />
      </MemoryRouter>,
    );

    expect(screen.getByText("GET")).toBeTruthy();
    expect(screen.getByText("POST")).toBeTruthy();
    expect(screen.getByText("+2 more")).toBeTruthy();
    expect(screen.queryByText("PUT")).toBeNull();
    expect(screen.queryByText("PATCH")).toBeNull();
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

  it("has one call to action in the empty state and hints at unlinked secrets on a row", () => {
    const { rerender } = renderWithProviders(
      <MemoryRouter>
        <ProxyList proxies={[]} isLoading={false} />
      </MemoryRouter>,
    );

    // Import lives in the page header, so the empty state offers a single button.
    expect(screen.getAllByRole("button")).toHaveLength(1);
    expect(screen.getByRole("button", { name: "Add proxy" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Import" })).toBeNull();

    rerender(
      <MemoryRouter>
        <ProxyList
          proxies={[{ ...PROXY_MOCK_DATA[0], hasUnboundSecrets: true }, PROXY_MOCK_DATA[1]]}
          isLoading={false}
        />
      </MemoryRouter>,
    );
    expect(screen.getAllByText("Check secrets")).toHaveLength(1);
  });

  it("says no proxies match when a filter is on, without a button of its own", () => {
    renderWithProviders(
      <MemoryRouter>
        <ProxyList proxies={[]} isLoading={false} isFiltered />
      </MemoryRouter>,
    );

    expect(screen.getByText("No proxies match this search.")).toBeTruthy();
    expect(screen.queryByRole("button")).toBeNull();
    expect(screen.queryByText("No proxies yet")).toBeNull();
  });

  it("shows as many skeleton rows as the page size while loading", () => {
    const { container } = renderWithProviders(
      <MemoryRouter>
        <ProxyList proxies={[]} isLoading rowCount={5} />
      </MemoryRouter>,
    );

    expect(container.querySelector("table")?.getAttribute("aria-busy")).toBe("true");
    expect(container.querySelectorAll("tbody tr")).toHaveLength(5);
  });

  it("keeps the toggle and the actions menu in their own columns and does not open the row from them", async () => {
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

    expect(screen.getByText("Row actions")).toBeTruthy();
    const row = screen.getByText("Stripe Payments").closest("tr")!;
    const toggle = within(row).getByLabelText("Stripe Payments enabled");
    expect(toggle.closest("td")).not.toBe(
      within(row).getByRole("button", { name: "Stripe Payments options" }).closest("td"),
    );

    await user.click(within(row).getByRole("button", { name: "Stripe Payments options" }));
    await user.click(await screen.findByRole("menuitem", { name: "Open" }));
    expect(screen.getByText("Stripe detail")).toBeTruthy();
  });
});

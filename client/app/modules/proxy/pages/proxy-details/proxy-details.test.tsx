import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";
vi.mock("../../services", async () => ({
  proxyService: (await import("../../test-support/mock-proxy-service")).mockProxyService,
}));

import { proxyService } from "../../services";
import { PROXY_MOCK_DATA } from "../../constants";
import { ProxyDetails } from "./proxy-details";

const mockProxyService = proxyService as unknown as {
  resetMockStore: () => void;
  get: (id: string) => Promise<unknown>;
  toggle: (payload: { id: string; enabled: boolean }) => Promise<unknown>;
};

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
    vi.restoreAllMocks();
    mockProxyService.resetMockStore();
    vi.clearAllMocks();
  });

  it("shows a route skeleton while the proxy is loading", async () => {
    let resolveGet: (value: unknown) => void = () => undefined;
    vi.spyOn(mockProxyService, "get").mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveGet = resolve;
        }),
    );

    renderWithProviders(
      <MemoryRouter initialEntries={["/proxy/p1"]}>
        <Routes>
          <Route path="/proxy/:proxyId" element={<ProxyDetails />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByRole("status", { name: "Loading proxy details" })).toBeTruthy();
    expect(screen.queryByText("Loading proxy...")).toBeNull();

    resolveGet(PROXY_MOCK_DATA[0]);
    expect(await screen.findByRole("heading", { name: "Stripe Payments" })).toBeTruthy();
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
    expect(screen.getByRole("tab", { name: "Request logs" })).toBeTruthy();
    expect(screen.getByRole("tab", { name: "Change history" })).toBeTruthy();
    expect(screen.queryByRole("tab", { name: /Request logs\s+\d+/i })).toBeNull();
    expect(screen.queryByRole("tab", { name: /Change history\s+\d+/i })).toBeNull();
  });

  it("shows selected response fields as a nested tree", async () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/proxy/p3"]}>
        <Routes>
          <Route path="/proxy/:proxyId" element={<ProxyDetails />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByRole("heading", { name: "Weather Lookup" })).toBeTruthy();
    expect(await screen.findByText(/Only 3 fields/)).toBeTruthy();
    expect(screen.getByText("location")).toBeTruthy();
    expect(screen.getByText("name")).toBeTruthy();
    expect(screen.getByText("current")).toBeTruthy();
    expect(screen.getByText("temp_c")).toBeTruthy();
    expect(screen.getByText("condition")).toBeTruthy();
    expect(screen.getByText("text")).toBeTruthy();
    expect(screen.queryByText("location.name")).toBeNull();
    expect(screen.queryByText("current.condition.text")).toBeNull();
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

  it("shows Resume for a paused proxy and toggles it on", async () => {
    const user = userEvent.setup();
    const toggleSpy = vi.spyOn(mockProxyService, "toggle");

    renderWithProviders(
      <MemoryRouter initialEntries={["/proxy/p3"]}>
        <Routes>
          <Route path="/proxy/:proxyId" element={<ProxyDetails />} />
        </Routes>
      </MemoryRouter>,
    );

    const resume = await screen.findByRole("button", { name: /resume/i });
    expect(screen.getByLabelText("Paused status indicator").className).toContain("bg-slate-400");
    expect(screen.getByLabelText("Paused status indicator").className).toContain(
      "dark:bg-slate-500",
    );
    expect(screen.getByText("Paused").className).toContain("bg-slate-200");
    expect(screen.getByText("Paused").className).toContain("dark:bg-slate-800");
    expect(screen.queryByRole("button", { name: /^pause$/i })).toBeNull();

    await user.click(resume);

    await waitFor(() => expect(toggleSpy).toHaveBeenCalledWith({ id: "p3", enabled: true }));
    await waitFor(() =>
      expect(toasts.showSuccessToast).toHaveBeenCalledWith({ description: "Proxy resumed." }),
    );
  });

  it("deletes the proxy from the details header and returns to the list", async () => {
    const user = userEvent.setup();

    renderWithProviders(
      <MemoryRouter initialEntries={["/proxy/p1"]}>
        <Routes>
          <Route path="/proxy/:proxyId" element={<ProxyDetails />} />
          <Route path="/app/item-123/proxy" element={<div>Proxy list route</div>} />
        </Routes>
      </MemoryRouter>,
    );

    await user.click(await screen.findByRole("button", { name: "Delete" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("heading", { name: "Delete Proxy" })).toBeTruthy();
    await user.click(within(dialog).getByRole("button", { name: "Delete" }));

    await waitFor(() =>
      expect(toasts.showSuccessToast).toHaveBeenCalledWith({
        description: "Proxy deleted successfully.",
      }),
    );
    expect(await screen.findByText("Proxy list route")).toBeTruthy();
  });

  it("shows pending text while pausing an active proxy", async () => {
    const user = userEvent.setup();
    let resolveToggle: (value: Awaited<ReturnType<typeof mockProxyService.toggle>>) => void = () =>
      undefined;
    vi.spyOn(mockProxyService, "toggle").mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveToggle = resolve;
        }),
    );

    renderWithProviders(
      <MemoryRouter initialEntries={["/proxy/p1"]}>
        <Routes>
          <Route path="/proxy/:proxyId" element={<ProxyDetails />} />
        </Routes>
      </MemoryRouter>,
    );

    await user.click(await screen.findByRole("button", { name: /^pause$/i }));

    expect(screen.getByRole("button", { name: /pausing/i })).toHaveProperty("disabled", true);

    resolveToggle({ isSuccess: true, itemId: "p1", errors: null });
    await waitFor(() =>
      expect(toasts.showSuccessToast).toHaveBeenCalledWith({ description: "Proxy paused." }),
    );
  });
});

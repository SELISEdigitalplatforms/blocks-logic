import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";

vi.mock("../../services", async () => ({
  proxyService: (await import("../../test-support/mock-proxy-service")).mockProxyService,
}));

import { proxyService } from "../../services";
import { PROXY_MOCK_DATA } from "../../constants";
import { Proxies } from "./proxies";

const mockProxyService = proxyService as unknown as {
  delete: (id: string) => Promise<unknown>;
  getAll: (params?: { pageNumber?: number; pageSize?: number }) => Promise<unknown>;
  resetMockStore: () => void;
};

describe("Proxies page", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    mockProxyService.resetMockStore();
  });

  it("renders the page title, updated copy, and proxy list", async () => {
    renderWithProviders(
      <MemoryRouter>
        <Proxies />
      </MemoryRouter>,
    );

    expect(await screen.findByRole("heading", { name: "Proxy" })).toBeTruthy();
    expect(screen.getByText(/Your client never\s+sees the vendor URL or the key/i)).toBeTruthy();
    expect(await screen.findByText("Stripe Payments")).toBeTruthy();
    expect(screen.getByRole("button", { name: /add proxy/i })).toBeTruthy();
  });

  it("hides the header add button when the proxy list is empty", async () => {
    await Promise.all(["p1", "p2", "p3"].map((id) => mockProxyService.delete(id)));

    renderWithProviders(
      <MemoryRouter>
        <Proxies />
      </MemoryRouter>,
    );

    const emptyState = await screen.findByText("No proxies yet");
    const pageHeader = screen.getByRole("heading", { name: "Proxy" }).closest("div");

    expect(emptyState).toBeTruthy();
    expect(screen.getAllByRole("button", { name: /add proxy/i })).toHaveLength(1);
    expect(
      pageHeader ? within(pageHeader).queryByRole("button", { name: /add proxy/i }) : null,
    ).toBeNull();
  });

  it("always offers Import, and Add proxy only when the list is not an empty project", async () => {
    await Promise.all(["p1", "p2", "p3"].map((id) => mockProxyService.delete(id)));
    const { unmount } = renderWithProviders(
      <MemoryRouter>
        <Proxies />
      </MemoryRouter>,
    );

    await screen.findByText("No proxies yet");
    expect(screen.getByRole("button", { name: "Import" })).toBeTruthy();
    unmount();

    mockProxyService.resetMockStore();
    renderWithProviders(
      <MemoryRouter>
        <Proxies />
      </MemoryRouter>,
    );
    await screen.findByText("Stripe Payments");
    expect(screen.getByRole("button", { name: "Import" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Add proxy" })).toBeTruthy();
  });

  it("keeps the header Add proxy button when a search matches nothing", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <MemoryRouter>
        <Proxies />
      </MemoryRouter>,
    );
    await screen.findByText("Stripe Payments");

    // The toolbar renders the search box twice (wide and narrow layouts); either one filters.
    await user.type(screen.getAllByPlaceholderText("Search...")[0], "zzz-no-such-proxy");

    expect(await screen.findByText("No proxies match this search.")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Add proxy" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Import" })).toBeTruthy();
  });

  it("shows an error card with Retry when the list cannot be loaded", async () => {
    const user = userEvent.setup();
    const getAll = vi.spyOn(mockProxyService, "getAll").mockRejectedValueOnce(new Error("boom"));
    renderWithProviders(
      <MemoryRouter>
        <Proxies />
      </MemoryRouter>,
    );

    expect(await screen.findByText("Proxies could not be loaded")).toBeTruthy();
    getAll.mockRestore();
    await user.click(screen.getByRole("button", { name: "Retry" }));
    expect(await screen.findByText("Stripe Payments")).toBeTruthy();
    expect(screen.queryByText("Proxies could not be loaded")).toBeNull();
  });

  it("hides the pager while everything fits on one page", async () => {
    renderWithProviders(
      <MemoryRouter>
        <Proxies />
      </MemoryRouter>,
    );

    expect(await screen.findByText("Stripe Payments")).toBeTruthy();
    expect(screen.queryByText(/Page \d+ of \d+/)).toBeNull();
    expect(screen.queryByText("Rows per page")).toBeNull();
  });

  it("pages through the list server-side once there is more than one page", async () => {
    const user = userEvent.setup();
    const getAll = vi
      .spyOn(mockProxyService, "getAll")
      .mockResolvedValue({ items: PROXY_MOCK_DATA, totalCount: 25 });

    const { container } = renderWithProviders(
      <MemoryRouter>
        <Proxies />
      </MemoryRouter>,
    );

    expect(await screen.findByText("Page 1 of 3")).toBeTruthy();

    getAll.mockClear();
    const nextButton = [...container.querySelectorAll("button")].find(
      (button) => button.querySelector(".lucide-chevron-right") && !button.disabled,
    );
    await user.click(nextButton!);

    await waitFor(() =>
      expect(getAll).toHaveBeenCalledWith(expect.objectContaining({ pageNumber: 1, pageSize: 10 })),
    );
    expect(await screen.findByText("Page 2 of 3")).toBeTruthy();
  });
});

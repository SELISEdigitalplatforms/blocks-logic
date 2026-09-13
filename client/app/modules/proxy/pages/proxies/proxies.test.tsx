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
    expect(screen.getByText(/The vendor URL and secret never reach the browser/i)).toBeTruthy();
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

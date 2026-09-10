import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";

vi.mock("../../services", async () => ({
  proxyService: (await import("../../test-support/mock-proxy-service")).mockProxyService,
}));

import { proxyService } from "../../services";
import { Proxies } from "./proxies";

const mockProxyService = proxyService as unknown as {
  delete: (id: string) => Promise<unknown>;
  resetMockStore: () => void;
};

describe("Proxies page", () => {
  beforeEach(() => {
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
});

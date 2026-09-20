import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
vi.mock("../services", async () => ({
  proxyService: (await import("../test-support/mock-proxy-service")).mockProxyService,
}));

import { proxyService } from "../services";
import { ProxyHistoryTab } from "./proxy-history-tab";

const mockProxyService = proxyService as unknown as { resetMockStore: () => void };

const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
}));

vi.mock("@/hooks/use-toast", () => toasts);

describe("ProxyHistoryTab", () => {
  beforeEach(() => {
    mockProxyService.resetMockStore();
    vi.clearAllMocks();
  });

  it("renders history rows and reverts non-delete versions", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProxyHistoryTab proxyId="p1" active={true} />);

    expect(await screen.findByText("Added payment intent expansion query")).toBeTruthy();
    expect(screen.queryByText(/Avery Stone/)).toBeNull();
    // an "add" change renders only the + line
    expect(screen.getByText(/\+ query expand\[\]: payment_intent/)).toBeTruthy();
    expect(screen.queryByText(/- query expand\[\]:/)).toBeNull();
    // a scalar change renders both - and + lines
    expect(screen.getByText(/- status: disabled/)).toBeTruthy();
    expect(screen.getByText(/\+ status: enabled/)).toBeTruthy();
    expect(screen.getAllByRole("button", { name: /revert/i })).toHaveLength(4);

    await user.click(screen.getAllByRole("button", { name: /revert/i })[0]);
    await waitFor(() =>
      expect(toasts.showSuccessToast).toHaveBeenCalledWith({
        description: "Proxy version reverted.",
      }),
    );
  });

  it("shows the server message when a revert conflicts", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProxyHistoryTab proxyId="p1" active={true} />);

    // First revert of the newest version removes the body field it added.
    await user.click((await screen.findAllByRole("button", { name: /revert/i }))[0]);
    await waitFor(() => expect(toasts.showSuccessToast).toHaveBeenCalled());

    // Reverting the same version again conflicts: the key no longer holds that version's "after" value.
    await user.click(screen.getAllByRole("button", { name: /revert/i })[0]);
    await waitFor(() =>
      expect(toasts.showErrorToast).toHaveBeenCalledWith({
        errors: expect.stringContaining("changed again in a later version"),
      }),
    );
  });

  it("renders a method-scoped override change", async () => {
    renderWithProviders(<ProxyHistoryTab proxyId="p1" active={true} />);

    expect(await screen.findByText("POST header X-Trace overridden")).toBeTruthy();
    expect(screen.getByText(/\+ POST header X-Trace: on/)).toBeTruthy();
  });

  it("renders the empty state when no history exists", async () => {
    renderWithProviders(<ProxyHistoryTab proxyId="p3" active={true} />);

    expect(await screen.findByText("No change history yet.")).toBeTruthy();
  });
});

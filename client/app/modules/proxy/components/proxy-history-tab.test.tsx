import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { proxyService } from "../services";
import { ProxyHistoryTab } from "./proxy-history-tab";

const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
}));

vi.mock("@/hooks/use-toast", () => toasts);

describe("ProxyHistoryTab", () => {
  beforeEach(() => {
    proxyService.resetMockStore();
    vi.clearAllMocks();
  });

  it("renders history rows and reverts non-delete versions", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProxyHistoryTab proxyId="p1" active={true} />);

    expect(await screen.findByText("Added payment intent expansion query.")).toBeTruthy();
    expect(screen.getByText("query: []")).toBeTruthy();

    await user.click(screen.getAllByRole("button", { name: /revert/i })[0]);
    await waitFor(() =>
      expect(toasts.showSuccessToast).toHaveBeenCalledWith({
        description: "Proxy version reverted.",
      }),
    );
  });

  it("renders the empty state when no history exists", async () => {
    renderWithProviders(<ProxyHistoryTab proxyId="p3" active={true} />);

    expect(await screen.findByText("No change history yet.")).toBeTruthy();
  });
});


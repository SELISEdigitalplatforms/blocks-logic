import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { PROXY_MOCK_DATA } from "../constants";
import { proxyService } from "../services";
import { ProxyLogsTab } from "./proxy-logs-tab";

const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
}));

vi.mock("@/hooks/use-toast", () => toasts);

describe("ProxyLogsTab", () => {
  beforeEach(() => {
    proxyService.resetMockStore();
    vi.clearAllMocks();
  });

  it("filters rows and expands log details", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProxyLogsTab proxy={PROXY_MOCK_DATA[0]} active={true} />);

    expect(await screen.findByText("3 of 3 requests")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "5xx" }));
    expect(await screen.findByText("1 of 3 requests")).toBeTruthy();
    await user.click(screen.getByText("/api/proxy/gateway/stripe-payments/charges"));
    expect(await screen.findByText(/Upstream timeout/)).toBeTruthy();
  });

  it("exports filtered rows as CSV", async () => {
    const user = userEvent.setup();
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:csv");
    vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);

    renderWithProviders(<ProxyLogsTab proxy={PROXY_MOCK_DATA[1]} active={true} />);

    await screen.findByText("1 of 1 requests");
    await user.click(screen.getByRole("button", { name: "5xx" }));
    await user.click(screen.getByRole("button", { name: /export csv/i }));

    await waitFor(() =>
      expect(toasts.showSuccessToast).toHaveBeenCalledWith({
        description: "0 requests exported as CSV.",
      }),
    );
  });
});

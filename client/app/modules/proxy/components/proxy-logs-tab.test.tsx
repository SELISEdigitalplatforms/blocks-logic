import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { PROXY_MOCK_DATA, PROXY_MOCK_EXECUTION_LOGS } from "../constants";

vi.mock("../services", async () => ({
  proxyService: (await import("../test-support/mock-proxy-service")).mockProxyService,
}));

import { proxyService } from "../services";
import { ProxyLogsTab } from "./proxy-logs-tab";

const mockProxyService = proxyService as unknown as { resetMockStore: () => void };

const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
}));

vi.mock("@/hooks/use-toast", () => toasts);

describe("ProxyLogsTab", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    mockProxyService.resetMockStore();
    vi.clearAllMocks();
  });

  it("shows visible loading skeletons while request logs load", () => {
    vi.spyOn(proxyService, "getExecutions").mockImplementation(
      () => new Promise(() => undefined),
    );

    renderWithProviders(<ProxyLogsTab proxy={PROXY_MOCK_DATA[0]} active={true} />);

    const loading = screen.getByRole("status", { name: "Loading request logs" });
    expect(loading.querySelector(".bg-slate-200")).toBeTruthy();
  });

  it("filters rows and expands log details", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProxyLogsTab proxy={PROXY_MOCK_DATA[0]} active={true} />);

    expect(await screen.findByText("3 of 3 requests")).toBeTruthy();
    expect(screen.getByRole("columnheader", { name: "METHOD" })).toBeTruthy();
    expect(screen.queryByRole("columnheader", { name: "METH" })).toBeNull();
    await user.click(screen.getByRole("button", { name: "5xx" }));
    expect(await screen.findByText("1 of 3 requests")).toBeTruthy();
    await user.click(screen.getByText("/api/proxy/gateway/stripe-payments/charges"));
    expect(await screen.findByText(/Upstream timeout/)).toBeTruthy();
  });

  it("paginates the logs table through the Pagination component", async () => {
    const user = userEvent.setup();
    const rows = PROXY_MOCK_EXECUTION_LOGS.filter((log) => log.proxyId === PROXY_MOCK_DATA[0].id);
    const getExecutions = vi
      .spyOn(proxyService, "getExecutions")
      .mockResolvedValue({ rows, totalCount: 25 });

    const { container } = renderWithProviders(
      <ProxyLogsTab proxy={PROXY_MOCK_DATA[0]} active={true} />,
    );

    expect(await screen.findByText("Page 1 of 3")).toBeTruthy();

    getExecutions.mockClear();
    const nextButton = [...container.querySelectorAll("button")].find(
      (button) => button.querySelector(".lucide-chevron-right") && !button.disabled,
    );
    await user.click(nextButton!);

    await waitFor(() =>
      expect(getExecutions).toHaveBeenCalledWith(
        PROXY_MOCK_DATA[0].id,
        "all",
        expect.objectContaining({ page: 1, pageSize: 10 }),
      ),
    );
    expect(await screen.findByText("Page 2 of 3")).toBeTruthy();
  });

  it("shows an empty state card without log controls when there are no requests", async () => {
    renderWithProviders(
      <ProxyLogsTab proxy={{ ...PROXY_MOCK_DATA[0], id: "proxy-without-logs" }} active={true} />,
    );

    expect(await screen.findByText("No request logs yet")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "All" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Live" })).toBeNull();
    expect(screen.queryByRole("columnheader", { name: "TIME" })).toBeNull();
  });
});

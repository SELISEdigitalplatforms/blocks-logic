import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { QueueStatusCard } from "./queue-status-card";

const getQueueStatus = vi.fn();
vi.mock("../../services/function.service", () => ({
  functionService: { getQueueStatus: (...args: unknown[]) => getQueueStatus(...args) },
}));

describe("QueueStatusCard", () => {
  beforeEach(() => vi.clearAllMocks());

  it("shows the HTTP and event counts and when the oldest event started waiting", async () => {
    getQueueStatus.mockResolvedValue({ httpQueued: 2, eventQueued: 7, oldestEventQueuedAt: new Date().toISOString() });

    renderWithProviders(<QueueStatusCard functionId="fn_1" />);

    expect(await screen.findByText("Waiting: 2 HTTP · 7 event")).toBeTruthy();
    expect(screen.getByText(/oldest event waiting since/)).toBeTruthy();
  });

  it("leaves the oldest line out when no event waits", async () => {
    getQueueStatus.mockResolvedValue({ httpQueued: 0, eventQueued: 0, oldestEventQueuedAt: null });

    renderWithProviders(<QueueStatusCard functionId="fn_1" />);

    expect(await screen.findByText("Waiting: 0 HTTP · 0 event")).toBeTruthy();
    expect(screen.queryByText(/oldest event/)).toBeNull();
  });
});

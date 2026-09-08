import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";

vi.mock("../services", async () => ({
  proxyService: (await import("../test-support/mock-proxy-service")).mockProxyService,
}));

import { proxyService } from "../services";
import { ProxyCountBadge } from "./proxy-count-badge";

const mockProxyService = proxyService as unknown as { resetMockStore: () => void };

describe("ProxyCountBadge", () => {
  beforeEach(() => {
    mockProxyService.resetMockStore();
  });

  it("renders the mock proxy count from the query hook", async () => {
    renderWithProviders(<ProxyCountBadge />);

    await waitFor(() => expect(screen.getByText("3")).toBeTruthy());
  });
});


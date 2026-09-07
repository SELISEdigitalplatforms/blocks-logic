import { beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { proxyService } from "../services";
import { ProxyCountBadge } from "./proxy-count-badge";

describe("ProxyCountBadge", () => {
  beforeEach(() => {
    proxyService.resetMockStore();
  });

  it("renders the mock proxy count from the query hook", async () => {
    renderWithProviders(<ProxyCountBadge />);

    await waitFor(() => expect(screen.getByText("3")).toBeTruthy());
  });
});


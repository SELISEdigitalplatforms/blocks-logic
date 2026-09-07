import { beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { proxyService } from "../../services";
import { Proxies } from "./proxies";

describe("Proxies page", () => {
  beforeEach(() => {
    proxyService.resetMockStore();
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
});

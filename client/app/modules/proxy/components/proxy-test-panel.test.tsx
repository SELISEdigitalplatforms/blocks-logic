import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { proxyService } from "../services";
import { ProxyTestPanel } from "./proxy-test-panel";

describe("ProxyTestPanel", () => {
  it("sends a saved proxy test request and renders success", async () => {
    const user = userEvent.setup();
    proxyService.resetMockStore();

    renderWithProviders(<ProxyTestPanel proxyId="p1" method="POST" />);

    await user.click(screen.getByRole("button", { name: /send test request/i }));
    expect(await screen.findByText("200 OK")).toBeTruthy();
    expect(screen.getByText(/mock proxy/i)).toBeTruthy();
  });

  it("renders a mock bad gateway response for invalid drafts", async () => {
    const user = userEvent.setup();

    renderWithProviders(
      <ProxyTestPanel
        draft={{
          name: "Bad",
          upstreamUrl: "http://example.com",
          methods: ["GET"],
          headers: [],
          query: [],
        }}
      />,
    );

    await user.click(screen.getByRole("button", { name: /send test request/i }));
    expect(await screen.findByText("502 Bad Gateway")).toBeTruthy();
  });
});


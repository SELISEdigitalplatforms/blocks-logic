import { beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { proxyService } from "../../services";
import { ProxyFormPage } from "./proxy-form-page";

describe("ProxyFormPage", () => {
  beforeEach(() => {
    proxyService.resetMockStore();
  });

  it("renders create and edit modes", async () => {
    const create = renderWithProviders(
      <MemoryRouter initialEntries={["/proxy/new"]}>
        <Routes>
          <Route path="/proxy/new" element={<ProxyFormPage mode="create" />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByRole("heading", { name: "Create Proxy" })).toBeTruthy();
    create.unmount();

    renderWithProviders(
      <MemoryRouter initialEntries={["/proxy/p1/edit"]}>
        <Routes>
          <Route path="/proxy/:proxyId/edit" element={<ProxyFormPage mode="edit" />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByRole("heading", { name: "Edit Proxy" })).toBeTruthy();
    expect(screen.getByDisplayValue("Stripe Payments")).toBeTruthy();
  });
});

import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { proxyService } from "../services";
import { ProxyForm } from "./proxy-form";

const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
}));

vi.mock("@/hooks/use-toast", () => toasts);

describe("ProxyForm", () => {
  beforeEach(() => {
    proxyService.resetMockStore();
    vi.clearAllMocks();
  });

  it("creates a valid proxy and calls onSuccess", async () => {
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="create" onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    await user.type(screen.getByPlaceholderText("Stripe Payments"), "Docs Proxy");
    await user.type(
      screen.getByPlaceholderText("https://api.example.com/v1/resource"),
      "https://api.example.com/docs",
    );
    await user.click(screen.getByRole("button", { name: "Create" }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    expect(toasts.showSuccessToast).toHaveBeenCalledWith({
      description: "Proxy created successfully.",
    });
  });

  it("edits and deletes an existing proxy", async () => {
    const user = userEvent.setup();
    const onDelete = vi.fn();
    const proxy = (await proxyService.get("p1"))!;

    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="edit" proxy={proxy} onDelete={onDelete} />
      </MemoryRouter>,
    );

    expect(screen.getByDisplayValue("Stripe Payments")).toBeTruthy();

    vi.spyOn(window, "confirm").mockReturnValue(true);
    await user.click(screen.getByRole("button", { name: "Delete" }));
    await waitFor(() => expect(onDelete).toHaveBeenCalled());
  });
});


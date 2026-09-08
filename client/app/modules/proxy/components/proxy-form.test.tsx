import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";
vi.mock("../services", async () => ({
  proxyService: (await import("../test-support/mock-proxy-service")).mockProxyService,
}));

import { proxyService } from "../services";
import { ProxyForm } from "./proxy-form";

const mockProxyService = proxyService as unknown as { resetMockStore: () => void };

const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
}));

vi.mock("@/hooks/use-toast", () => toasts);

describe("ProxyForm", () => {
  beforeEach(() => {
    mockProxyService.resetMockStore();
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

    fireEvent.change(screen.getByPlaceholderText("Enter name"), {
      target: { value: "Docs Proxy" },
    });
    fireEvent.change(screen.getByPlaceholderText("Enter third-party endpoint"), {
      target: { value: "https://api.example.com/docs" },
    });
    await user.click(screen.getByRole("button", { name: "Create" }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    expect(toasts.showSuccessToast).toHaveBeenCalledWith({
      description: "Proxy created successfully.",
    });
  });

  it("keeps only one selected method when creating a proxy", async () => {
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="create" onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    fireEvent.change(screen.getByPlaceholderText("Enter name"), {
      target: { value: "Docs Proxy" },
    });
    fireEvent.change(screen.getByPlaceholderText("Enter third-party endpoint"), {
      target: { value: "https://api.example.com/docs" },
    });
    await user.click(screen.getByRole("button", { name: "POST" }));
    await user.click(screen.getByRole("button", { name: "Create" }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    const proxy = await proxyService.get(onSuccess.mock.calls[0][0]);
    expect(proxy?.methods).toEqual(["POST"]);
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

  it("shows an inline url validation message instead of relying on native validation", async () => {
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="create" onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    fireEvent.change(screen.getByPlaceholderText("Enter name"), {
      target: { value: "Docs Proxy" },
    });
    fireEvent.change(screen.getByPlaceholderText("Enter third-party endpoint"), {
      target: { value: "jsonplaceholder.typicode.com/users" },
    });
    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(await screen.findByText("Please enter a valid url")).toBeTruthy();
    expect(onSuccess).not.toHaveBeenCalled();
  });
});

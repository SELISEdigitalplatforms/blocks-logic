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

  it("shows the Request body card only when a POST/PUT/PATCH method is selected", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="create" onSuccess={vi.fn()} />
      </MemoryRouter>,
    );

    // default methods === ["GET"] -> no card
    expect(screen.queryByText("Request body")).toBeNull();

    await user.click(screen.getByRole("button", { name: "POST" }));
    expect(screen.getByText("Request body")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "GET" }));
    expect(screen.queryByText("Request body")).toBeNull();
  });

  it("seeds the body tab to merge when editing a proxy that has body fields", async () => {
    const proxy = (await proxyService.get("p1"))!; // p1 mock has a non-empty bodyMerge

    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="edit" proxy={proxy} />
      </MemoryRouter>,
    );

    // merge tab active -> the existing body rows are rendered
    expect(await screen.findByDisplayValue("account")).toBeTruthy();
  });

  it("persists bodyMerge: [] when body fields are typed then the tab is switched to Pass through", async () => {
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="create" onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    fireEvent.change(screen.getByPlaceholderText("Enter name"), {
      target: { value: "Body Proxy" },
    });
    fireEvent.change(screen.getByPlaceholderText("Enter third-party endpoint"), {
      target: { value: "https://api.example.com/x" },
    });
    await user.click(screen.getByRole("button", { name: "POST" }));
    await user.click(screen.getByRole("button", { name: "Merge fields" }));
    await user.click(screen.getByRole("button", { name: /add field/i }));
    fireEvent.change(screen.getByPlaceholderText("Enter key"), { target: { value: "account" } });
    fireEvent.change(screen.getByPlaceholderText("Enter value"), { target: { value: "acct_1" } });
    await user.click(screen.getByRole("button", { name: "Pass through" }));
    await user.click(screen.getByRole("button", { name: "Create" }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    const saved = await proxyService.get(onSuccess.mock.calls[0][0]);
    expect(saved?.bodyMerge).toEqual([]);
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

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

const fillConnection = (name: string, url: string) => {
  fireEvent.change(screen.getByPlaceholderText("Enter name"), { target: { value: name } });
  fireEvent.change(screen.getByPlaceholderText("https://api.vendor.com"), {
    target: { value: url },
  });
};

describe("ProxyForm", () => {
  beforeEach(() => {
    mockProxyService.resetMockStore();
    vi.clearAllMocks();
  });

  it("creates a proxy with one base-path endpoint and methods derived from it", async () => {
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="create" onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    fillConnection("Docs Proxy", "https://api.example.com/docs");
    await user.click(screen.getByRole("button", { name: "Create" }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    expect(toasts.showSuccessToast).toHaveBeenCalledWith({
      description: "Proxy created successfully.",
    });

    const saved = await proxyService.get(onSuccess.mock.calls[0][0]);
    // The simple case needs no endpoint setup: a proxy starts one-to-one with its base path.
    expect(saved?.routes).toHaveLength(1);
    expect(saved?.routes[0]).toMatchObject({ method: "GET", path: "" });
    // Methods are not chosen; they are whatever the endpoints use.
    expect(saved?.methods).toEqual(["GET"]);
    // Nothing is configured at proxy level any more.
    expect(saved?.bodyMerge).toEqual([]);
    expect(saved?.responseMode).toBe("all");
    expect(saved?.methodConfigs).toEqual([]);
  });

  it("stores credential rows as headers or query by their delivery slot", async () => {
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="create" onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    fillConnection("Keyed Proxy", "https://api.example.com");
    // "Add" is the credential list's button; the endpoint card's is "Add endpoint".
    await user.click(screen.getByRole("button", { name: "Add" }));
    fireEvent.change(screen.getByPlaceholderText("Enter key"), {
      target: { value: "Authorization" },
    });
    fireEvent.change(screen.getByPlaceholderText("Enter value"), {
      target: { value: "Bearer secret" },
    });
    await user.click(screen.getByRole("button", { name: "Create" }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    const saved = await proxyService.get(onSuccess.mock.calls[0][0]);
    // Default delivery slot is a header, which is what almost every vendor takes.
    expect(saved?.headers).toEqual([{ key: "Authorization", value: "Bearer secret" }]);
    expect(saved?.query).toEqual([]);
  });

  it("filters response fields per endpoint, not per proxy", async () => {
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="create" onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    fillConnection("Filtered Proxy", "https://api.example.com");
    await user.click(screen.getByRole("button", { name: "What this endpoint sends and returns" }));
    await user.click(screen.getByRole("switch", { name: "Filter response fields for endpoint 1" }));
    await user.click(screen.getByRole("button", { name: "Add field" }));
    fireEvent.change(screen.getByLabelText("Response field 1 for endpoint 1"), {
      target: { value: "data.id" },
    });
    await user.click(screen.getByRole("button", { name: "Create" }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    const saved = await proxyService.get(onSuccess.mock.calls[0][0]);
    expect(saved?.routes[0]).toMatchObject({ responseMode: "select", responseInclude: ["data.id"] });
    // The proxy-level filter stays off: the endpoint owns this decision.
    expect(saved?.responseMode).toBe("all");
    expect(saved?.responseInclude).toEqual([]);
  });

  it("edits an existing proxy, seeding its credential from headers, and has no delete action", async () => {
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    const proxy = (await proxyService.get("p1"))!;

    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="edit" proxy={proxy} onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    expect(screen.getByDisplayValue("Stripe Payments")).toBeTruthy();
    // The connection's stored headers come back as credential rows.
    expect(screen.getByDisplayValue("Authorization")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Delete" })).toBeNull();

    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
  });

  it("will not remove the last endpoint", async () => {
    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="create" onSuccess={vi.fn()} />
      </MemoryRouter>,
    );

    // No jest-dom in this project, so read the property rather than using toBeDisabled.
    expect(screen.getByRole("button", { name: "Remove endpoint 1" })).toHaveProperty("disabled", true);
  });

  it("shows an inline url validation message instead of relying on native validation", async () => {
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="create" onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    fillConnection("Docs Proxy", "jsonplaceholder.typicode.com/users");
    await user.click(screen.getByRole("button", { name: "Create" }));

    expect(await screen.findByText("Please enter a valid url")).toBeTruthy();
    expect(onSuccess).not.toHaveBeenCalled();
  });
});

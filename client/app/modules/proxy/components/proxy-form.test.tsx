import { useState } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
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

  it("edits an existing proxy and has no delete action", async () => {
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    const proxy = (await proxyService.get("p1"))!;

    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="edit" proxy={proxy} onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    expect(screen.getByDisplayValue("Stripe Payments")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Delete" })).toBeNull();

    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
  });

  it("edits response fields and saves the NEW selection, not the originally loaded one", async () => {
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    const proxy = (await proxyService.get("p3"))!; // select mode, 3 response paths

    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="edit" proxy={proxy} onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    // tree seeds from responseInclude: location.name / current.temp_c / current.condition.text
    const nameInput = await screen.findByDisplayValue("name");
    await user.click(within(nameInput.closest("div")!).getByRole("checkbox"));

    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(onSuccess).toHaveBeenCalled());

    const saved = await proxyService.get("p3");
    expect(saved?.responseInclude).toEqual(
      expect.arrayContaining(["current.temp_c", "current.condition.text"]),
    );
    expect(saved?.responseInclude).not.toContain("location.name");
  });

  it("keeps response edits when the proxy prop gets a fresh reference (React Query refetch)", async () => {
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    const base = (await proxyService.get("p3"))!;

    const Wrapper = () => {
      const [proxy, setProxy] = useState(base);
      return (
        <MemoryRouter>
          <button type="button" onClick={() => setProxy({ ...base })}>
            refetch
          </button>
          <ProxyForm mode="edit" proxy={proxy} onSuccess={onSuccess} />
        </MemoryRouter>
      );
    };

    renderWithProviders(<Wrapper />);

    const nameInput = await screen.findByDisplayValue("name");
    await user.click(within(nameInput.closest("div")!).getByRole("checkbox"));

    // simulate a background refetch handing down a new object with identical content
    await user.click(screen.getByRole("button", { name: "refetch" }));

    // the unchecked field must NOT come back
    expect(screen.queryByDisplayValue("name")).toBeTruthy(); // row still there
    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(onSuccess).toHaveBeenCalled());

    const saved = await proxyService.get("p3");
    expect(saved?.responseInclude).not.toContain("location.name");
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
    await user.click(screen.getByRole("tab", { name: "Merge fields" }));
    await user.click(screen.getByRole("button", { name: /add field/i }));
    fireEvent.change(screen.getByPlaceholderText("Enter key"), { target: { value: "account" } });
    fireEvent.change(screen.getByPlaceholderText("Enter value"), { target: { value: "acct_1" } });
    await user.click(screen.getByRole("tab", { name: "Pass through" }));
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

import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";
vi.mock("../services", async () => ({
  proxyService: (await import("../test-support/mock-proxy-service")).mockProxyService,
}));

vi.mock("@/services/secret.service", async () => ({
  secretService: (await import("../test-support/mock-secret-service")).mockSecretService,
}));

// The "Who can call it" card lists the tenant's roles / permissions; the form tests are not about that.
vi.mock("@/modules/workflow/services/iam.service", () => ({
  iamService: {
    getRoles: vi.fn(async () => ({ data: [], totalCount: 0, errors: null })),
    getPermissions: vi.fn(async () => ({ data: [], totalCount: 0, errors: null })),
  },
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
    await user.click(screen.getByRole("button", { name: "Save" }));

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

  it("saves a timeout the user configured, and nothing when they configure nothing", async () => {
    // Both halves matter. The console sends every field back on save, so a number that appeared on
    // its own would be stored as though somebody had chosen it.
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="create" onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    fillConnection("Slow Vendor", "https://api.example.com");
    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    expect((await proxyService.get(onSuccess.mock.calls[0][0]))?.resilience).toBeNull();

    await user.click(screen.getByLabelText("Set a timeout for this proxy"));
    fireEvent.change(screen.getByLabelText("Seconds"), { target: { value: "8" } });
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalledTimes(2));
    expect((await proxyService.get(onSuccess.mock.calls[1][0]))?.resilience).toEqual({
      timeoutSeconds: 8,
      retry: null,
      breaker: null,
    });
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
    await user.click(screen.getByRole("button", { name: "Save" }));

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
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    const saved = await proxyService.get(onSuccess.mock.calls[0][0]);
    expect(saved?.routes[0]).toMatchObject({
      responseMode: "select",
      responseInclude: ["data.id"],
    });
    // The proxy-level filter stays off: the endpoint owns this decision.
    expect(saved?.responseMode).toBe("all");
    expect(saved?.responseInclude).toEqual([]);
  });

  it("keeps a proxy-wide response filter set outside the console when the proxy is saved", async () => {
    // Sending "all" here would silently drop the filter and relay fields the owner chose to hide.
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    const p1 = (await proxyService.get("p1"))!;
    const proxy = { ...p1, responseMode: "select" as const, responseInclude: ["data.id"] };

    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="edit" proxy={proxy} onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    expect(screen.getByText("Proxy-wide response filter is on")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    const saved = await proxyService.get("p1");
    expect(saved?.responseMode).toBe("select");
    expect(saved?.responseInclude).toEqual(["data.id"]);
  });

  it("turns the proxy-wide response filter off only when the owner asks", async () => {
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    const p1 = (await proxyService.get("p1"))!;
    const proxy = { ...p1, responseMode: "select" as const, responseInclude: ["data.id"] };

    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="edit" proxy={proxy} onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    await user.click(
      screen.getByRole("button", { name: "Turn off: Proxy-wide response filter is on" }),
    );
    expect(screen.queryByText("Proxy-wide response filter is on")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    const saved = await proxyService.get("p1");
    expect(saved?.responseMode).toBe("all");
    expect(saved?.responseInclude).toEqual([]);
  });

  it("keeps proxy-wide body fields and per-method overrides set outside the console on save", async () => {
    // p1 carries both, set through the API. A save that sent them empty would change what the vendor
    // receives (body fields) and where POST calls go (per-method upstream).
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    const proxy = (await proxyService.get("p1"))!;
    expect(proxy.bodyMerge.length).toBeGreaterThan(0);
    expect(proxy.methodConfigs.length).toBeGreaterThan(0);

    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="edit" proxy={proxy} onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    expect(screen.getByText("Proxy-wide body fields are on")).toBeTruthy();
    expect(screen.getByText("Per-method overrides are on")).toBeTruthy();
    // Body-merge values can hold a credential; the card names keys only.
    for (const row of proxy.bodyMerge) {
      expect(screen.queryByText(row.value)).toBeNull();
    }
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    const saved = await proxyService.get("p1");
    expect(saved?.bodyMerge).toEqual(proxy.bodyMerge);
    expect(saved?.methodConfigs).toEqual(proxy.methodConfigs);
  });

  it("clears proxy-wide body fields and per-method overrides only when the owner turns them off", async () => {
    const user = userEvent.setup();
    const onSuccess = vi.fn();
    const proxy = (await proxyService.get("p1"))!;

    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="edit" proxy={proxy} onSuccess={onSuccess} />
      </MemoryRouter>,
    );

    await user.click(screen.getByRole("button", { name: "Turn off: Proxy-wide body fields are on" }));
    await user.click(screen.getByRole("button", { name: "Turn off: Per-method overrides are on" }));
    expect(screen.queryByTestId("proxy-api-only-settings")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    const saved = await proxyService.get("p1");
    expect(saved?.bodyMerge).toEqual([]);
    expect(saved?.methodConfigs).toEqual([]);
  });

  it("shows no proxy-wide settings card for a new proxy", () => {
    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="create" />
      </MemoryRouter>,
    );
    expect(screen.queryByTestId("proxy-api-only-settings")).toBeNull();
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
    expect(screen.getByRole("button", { name: "Remove endpoint 1" })).toHaveProperty(
      "disabled",
      true,
    );
  });

  it("inserts configuration variables into endpoint override values", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <MemoryRouter>
        <ProxyForm mode="create" onSuccess={vi.fn()} />
      </MemoryRouter>,
    );

    await user.click(screen.getByRole("button", { name: "What this endpoint sends and returns" }));
    await user.click(screen.getByRole("switch", { name: "Extra headers for endpoint 1" }));
    await user.type(screen.getByPlaceholderText("Enter value"), "Bearer ");
    await user.click(
      await screen.findByRole("button", {
        name: "Insert a configuration variable into extra headers value",
      }),
    );
    await user.click(await screen.findByRole("option", { name: /stripe-api-key/ }));

    expect(screen.getByDisplayValue("Bearer {{$VAR.stripe-api-key}}")).toBeTruthy();
    expect(screen.getByText(/uses a variable/i)).toBeTruthy();
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
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Please enter a valid url")).toBeTruthy();
    expect(onSuccess).not.toHaveBeenCalled();
  });
});

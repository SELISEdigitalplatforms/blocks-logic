import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { FieldSchema, ReadonlyDetails } from "../form-field.types";
import { ReadonlyDetailsField } from "./readonly-details-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { tenantId: "pk", workflowId: "wf", nodeId: "n1" };

const deferred = <T,>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((res) => (resolve = res));
  return { promise, resolve };
};

const DETAILS: ReadonlyDetails = {
  fields: [
    { field: { id: "rc-upstream", key: "u", type: "text", label: "Forwards to" }, value: "https://api.vendor.test/orders" },
    { field: { id: "rc-select", key: "s", type: "switch", label: "Return selected fields only" }, value: true },
    {
      field: { id: "rc-headers", key: "h", type: "key-value-pairs", label: "Headers added" },
      value: { Authorization: "{{$VAR.api-key}}" },
    },
    { field: { id: "rc-query", key: "q", type: "key-value-pairs", label: "Query parameters added" }, value: {} },
  ],
  link: { label: "Edit proxy", path: "proxy/p1/edit" },
};

const field = (details: FieldSchema["details"]): FieldSchema => ({
  id: "routeConfig",
  key: "routeConfig",
  type: "readonly-details",
  transient: true,
  details,
  detailsDependencies: ["routePath"],
});

describe("ReadonlyDetailsField", () => {
  it("shows a loading state, then each setting in its own locked form field", async () => {
    const pending = deferred<ReadonlyDetails>();
    renderWithProviders(
      <ReadonlyDetailsField
        field={field(() => pending.promise)}
        value={undefined}
        onChange={vi.fn()}
        data={{ routePath: "orders" }}
        config={cfg}
      />,
    );

    expect(screen.getByText("Loading details...")).toBeTruthy();
    pending.resolve(DETAILS);

    const upstream = (await screen.findByDisplayValue(
      "https://api.vendor.test/orders",
    )) as HTMLInputElement;
    expect(upstream.disabled || upstream.readOnly).toBe(true);
    expect(screen.getByText("Forwards to")).toBeTruthy();

    const toggle = screen.getByRole("switch");
    expect(toggle.getAttribute("aria-checked")).toBe("true");
    expect((toggle as HTMLButtonElement).disabled).toBe(true);

    expect(screen.getByDisplayValue("Authorization")).toBeTruthy();
    expect(screen.getByDisplayValue("{{$VAR.api-key}}")).toBeTruthy();
    // An empty list reads "None" instead of rendering nothing.
    expect(screen.getByText("Query parameters added")).toBeTruthy();
    expect(screen.getByText("None")).toBeTruthy();
    // Locked: no secret picker on any input.
    expect(screen.queryByRole("button", { name: /configuration variable/i })).toBeNull();

    const link = screen.getByRole("link", { name: /edit proxy/i });
    // Resolved through useScopedPath, which the test stub leaves as the scope-relative path.
    expect(link.getAttribute("href")).toMatch(/proxy\/p1\/edit$/);
    expect(link.getAttribute("target")).toBe("_blank");
  });

  it("shows the message when there are no fields", async () => {
    renderWithProviders(
      <ReadonlyDetailsField
        field={field(async () => ({ fields: [], message: "This endpoint no longer exists." }))}
        value={undefined}
        onChange={vi.fn()}
        data={{ routePath: "a" }}
        config={cfg}
      />,
    );

    expect(await screen.findByText("This endpoint no longer exists.")).toBeTruthy();
  });

  it("re-runs the loader when a dependency changes, and only then", async () => {
    const details = vi.fn(async (data: Record<string, unknown>) => ({
      fields: [
        {
          field: { id: "rc-up", key: "u", type: "text" as const, label: "Forwards to" },
          value: `GET /${String(data.routePath)}`,
        },
      ],
    }));
    const props = { value: undefined, onChange: vi.fn(), config: cfg };
    const { rerender } = renderWithProviders(
      <ReadonlyDetailsField field={field(details)} data={{ routePath: "a" }} {...props} />,
    );
    expect(await screen.findByDisplayValue("GET /a")).toBeTruthy();

    // An unrelated parameter changing does not refetch.
    rerender(
      <ReadonlyDetailsField
        field={field(details)}
        data={{ routePath: "a", other: 1 }}
        {...props}
      />,
    );
    expect(details).toHaveBeenCalledTimes(1);

    rerender(<ReadonlyDetailsField field={field(details)} data={{ routePath: "b" }} {...props} />);
    expect(await screen.findByDisplayValue("GET /b")).toBeTruthy();
    expect(details).toHaveBeenCalledTimes(2);
  });

  it("shows an error state when the loader fails", async () => {
    renderWithProviders(
      <ReadonlyDetailsField
        field={field(() => Promise.reject(new Error("boom")))}
        value={undefined}
        onChange={vi.fn()}
        data={{ routePath: "a" }}
        config={cfg}
      />,
    );

    expect(await screen.findByText("Could not load these details.")).toBeTruthy();
  });
});

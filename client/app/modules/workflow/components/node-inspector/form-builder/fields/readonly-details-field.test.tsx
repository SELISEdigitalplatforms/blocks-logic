import { describe, expect, it, vi } from "vitest";
import { screen, within } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { DetailSection, FieldSchema } from "../form-field.types";
import { ReadonlyDetailsField } from "./readonly-details-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { tenantId: "pk", workflowId: "wf", nodeId: "n1" };

const deferred = <T,>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((res) => (resolve = res));
  return { promise, resolve };
};

const SECTIONS: DetailSection[] = [
  { title: "Forwards to", text: "GET https://api.vendor.test/orders" },
  {
    title: "Headers added",
    rows: [
      { key: "Authorization", value: "{{$VAR.api-key}}", tag: "connection" },
      { key: "X-Trace", value: "on", tag: "endpoint" },
    ],
  },
  { title: "Query parameters added", note: "Win over a row below.", rows: [] },
  { title: "Response", text: "Only these fields:", items: ["data.id"] },
  { link: { label: "Edit proxy", path: "proxy/p1/edit" } },
];

const field = (details: FieldSchema["details"]): FieldSchema => ({
  id: "routeConfig",
  key: "routeConfig",
  type: "readonly-details",
  transient: true,
  details,
  detailsDependencies: ["routePath"],
});

describe("ReadonlyDetailsField", () => {
  it("shows a loading state, then the sections as locked rows", async () => {
    const pending = deferred<DetailSection[]>();
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
    pending.resolve(SECTIONS);

    expect(await screen.findByText("GET https://api.vendor.test/orders")).toBeTruthy();
    const headers = screen.getByRole("list", { name: "Headers added" });
    expect(within(headers).getByText("{{$VAR.api-key}}")).toBeTruthy();
    expect(within(headers).getByText("variable")).toBeTruthy();
    expect(within(headers).getByText("connection")).toBeTruthy();
    expect(within(headers).getByText("endpoint")).toBeTruthy();
    // No inputs: the rows are display only.
    expect(screen.queryByRole("textbox")).toBeNull();

    expect(screen.getByText("Win over a row below.")).toBeTruthy();
    expect(screen.getByText("None")).toBeTruthy();
    expect(screen.getByText("data.id")).toBeTruthy();

    const link = screen.getByRole("link", { name: /edit proxy/i });
    // Resolved through useScopedPath, which the test stub leaves as the scope-relative path.
    expect(link.getAttribute("href")).toMatch(/proxy\/p1\/edit$/);
    expect(link.getAttribute("target")).toBe("_blank");
  });

  it("re-runs the loader when a dependency changes, and only then", async () => {
    const details = vi.fn(async (data: Record<string, unknown>) => [
      { title: "Forwards to", text: `GET /${String(data.routePath)}` },
    ]);
    const props = { value: undefined, onChange: vi.fn(), config: cfg };
    const { rerender } = renderWithProviders(
      <ReadonlyDetailsField field={field(details)} data={{ routePath: "a" }} {...props} />,
    );
    expect(await screen.findByText("GET /a")).toBeTruthy();

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
    expect(await screen.findByText("GET /b")).toBeTruthy();
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

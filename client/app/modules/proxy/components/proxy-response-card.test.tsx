import { useEffect } from "react";
import { describe, expect, it, vi } from "vitest";
import { useForm } from "react-hook-form";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Form } from "@/components/ui-kits/form/form";
import { ProxyFormValues, SampleResult } from "../types";
import { ProxyResponseCard } from "./proxy-response-card";

const okSample = (over: Partial<SampleResult> = {}): SampleResult => ({
  ok: true,
  status: 200,
  contentType: "application/json",
  body: JSON.stringify({ data: { id: 1, name: "x" }, meta: { page: 1 } }),
  bytes: 40,
  ...over,
});

const Harness = ({
  runSample = vi.fn(async () => okSample()),
  responseMode = "select" as ProxyFormValues["responseMode"],
  responseInclude = [] as string[],
}: {
  runSample?: () => Promise<SampleResult>;
  responseMode?: ProxyFormValues["responseMode"];
  responseInclude?: string[];
}) => {
  const form = useForm<ProxyFormValues>({
    defaultValues: {
      name: "P",
      upstreamUrl: "https://api.x.com",
      methods: ["GET"],
      headers: [],
      query: [],
      bodyMerge: [],
      bodyMode: "passthrough",
      methodConfigs: [],
      responseMode,
      responseInclude,
    },
  });
  const paths = form.watch("responseInclude");
  const mode = form.watch("responseMode");
  return (
    <Form {...form}>
      <ProxyResponseCard control={form.control} runSample={runSample} seedKey="new" />
      <pre data-testid="paths">{JSON.stringify(paths)}</pre>
      <pre data-testid="mode">{mode}</pre>
    </Form>
  );
};

/** Mirrors edit mode: the form mounts empty, then `form.reset` lands the saved proxy a tick later. */
const EditHarness = ({ resetTo }: { resetTo: string[] }) => {
  const form = useForm<ProxyFormValues>({
    defaultValues: {
      name: "P",
      upstreamUrl: "https://api.x.com",
      methods: ["GET"],
      headers: [],
      query: [],
      bodyMerge: [],
      bodyMode: "passthrough",
      methodConfigs: [],
      responseMode: "all",
      responseInclude: [],
    },
  });
  useEffect(() => {
    form.reset({
      name: "P",
      upstreamUrl: "https://api.x.com",
      methods: ["GET"],
      headers: [],
      query: [],
      bodyMerge: [],
      bodyMode: "passthrough",
      methodConfigs: [],
      responseMode: "select",
      responseInclude: resetTo,
    });
  }, [form, resetTo]);
  const paths = form.watch("responseInclude");
  return (
    <Form {...form}>
      <ProxyResponseCard control={form.control} runSample={vi.fn()} seedKey="proxy-1" />
      <pre data-testid="paths">{JSON.stringify(paths)}</pre>
    </Form>
  );
};

const paths = () => JSON.parse(screen.getByTestId("paths").textContent || "[]") as string[];
const mode = () => screen.getByTestId("mode").textContent;

/**
 * The row wrapper for a field, found from its name input. The input sits inside its own
 * label/error column, so `closest("div")` lands on that column rather than the row that also
 * holds the checkbox and the add/remove buttons — scope by the row's test id instead.
 */
const rowOf = (value: string) =>
  screen.getByDisplayValue(value).closest("[data-testid='response-field-row']") as HTMLElement;

describe("ProxyResponseCard", () => {
  it("shows the passthrough panel on 'All fields' and keeps the tree lossless across tabs", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness responseMode="all" responseInclude={["data.id"]} />);

    expect(screen.getByText(/relayed to your client unchanged/i)).toBeTruthy();

    await user.click(screen.getByRole("tab", { name: "Filter fields" }));
    expect(screen.getByDisplayValue("data")).toBeTruthy();
    expect(screen.getByDisplayValue("id")).toBeTruthy();

    await user.click(screen.getByRole("tab", { name: "All fields" }));
    await user.click(screen.getByRole("tab", { name: "Filter fields" }));
    expect(screen.getByDisplayValue("data")).toBeTruthy();
  });

  it("adds a root field, names it and encodes it onto the form", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness />);

    await user.click(screen.getByRole("button", { name: /add root field/i }));
    await user.type(screen.getByPlaceholderText("field name"), "status");
    await user.tab();

    expect(paths()).toEqual(["status"]);
  });

  it("expands a subtree-covered parent when a child is added by hand", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness responseInclude={["data"]} />);

    // "data" covers its whole subtree. Adding an explicit child must narrow to that child's
    // path instead of the encoding swallowing it back into ["data"].
    const dataRow = rowOf("data");
    await user.click(within(dataRow).getByRole("button", { name: /add child field/i }));
    const blank = screen
      .getAllByPlaceholderText("field name")
      .find((input) => (input as HTMLInputElement).value === "")!;
    await user.type(blank, "id");
    await user.tab();

    expect(paths()).toEqual(["data.id"]);
  });

  it("one click clears a selected parent's subtree, the next re-selects it as a single path", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness responseInclude={["data.id", "data.name"]} />);

    expect(paths().sort()).toEqual(["data.id", "data.name"]);
    const dataRow = rowOf("data");

    // Parent reads as selected (children checked) — one click clears the whole subtree.
    await user.click(within(dataRow).getByRole("checkbox"));
    expect(paths()).toEqual([]);

    // Now fully unchecked — checking the parent collapses to a single minimal path.
    await user.click(within(dataRow).getByRole("checkbox"));
    expect(paths()).toEqual(["data"]);
  });

  it("unchecking every node keeps select mode with an empty list and an amber caption", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Harness responseInclude={["data.id"]} />);

    const idRow = rowOf("id");
    await user.click(within(idRow).getByRole("checkbox"));

    expect(paths()).toEqual([]);
    expect(mode()).toBe("select");
    expect(screen.getByText(/Nothing selected — clients receive/i)).toBeTruthy();
  });

  it("'Fill from test run' merges the discovered schema into the tree", async () => {
    const user = userEvent.setup();
    const runSample = vi.fn(async () => okSample());
    renderWithProviders(<Harness runSample={runSample} />);

    await user.click(screen.getByRole("button", { name: /fill from test run/i }));

    expect(runSample).toHaveBeenCalledOnce();
    expect(await screen.findByText(/Filled \d+ fields/i)).toBeTruthy();
    expect(screen.getByDisplayValue("data")).toBeTruthy();
    expect(paths().length).toBeGreaterThan(0);
  });

  it("rejects an oversized sample without parsing it", async () => {
    const user = userEvent.setup();
    const parseSpy = vi.spyOn(JSON, "parse");
    const runSample = vi.fn(async () =>
      okSample({ bytes: 6 * 1024 * 1024, body: "{}" }),
    );
    renderWithProviders(<Harness runSample={runSample} />);

    await user.click(screen.getByRole("button", { name: /fill from test run/i }));

    expect(await screen.findByText(/over the 5 MB filter limit/i)).toBeTruthy();
    expect(parseSpy).not.toHaveBeenCalledWith("{}");
    parseSpy.mockRestore();
  });

  it("notes a non-2xx sample and leaves the tree untouched", async () => {
    const user = userEvent.setup();
    const runSample = vi.fn(async () => okSample({ ok: false, status: 503, error: undefined }));
    renderWithProviders(<Harness runSample={runSample} responseInclude={["data.id"]} />);

    await user.click(screen.getByRole("button", { name: /fill from test run/i }));

    expect(await screen.findByText(/Endpoint returned 503/i)).toBeTruthy();
    expect(paths()).toEqual(["data.id"]);
  });

  it("a primitive-root sample locks the whole-response checkbox and forces 'all'", async () => {
    const user = userEvent.setup();
    const runSample = vi.fn(async () => okSample({ body: '"just-a-string"' }));
    renderWithProviders(<Harness runSample={runSample} />);

    await user.click(screen.getByRole("button", { name: /fill from test run/i }));

    expect(await screen.findByText(/Whole response — nothing to filter/i)).toBeTruthy();
    expect(mode()).toBe("all");
  });

  it("seeds the field tree when the parent's form.reset lands after mount (edit mode)", async () => {
    renderWithProviders(<EditHarness resetTo={["data.id", "data.name"]} />);

    expect(await screen.findByDisplayValue("data")).toBeTruthy();
    expect(screen.getByDisplayValue("id")).toBeTruthy();
    expect(screen.getByDisplayValue("name")).toBeTruthy();
    await waitFor(() => expect(paths().sort()).toEqual(["data.id", "data.name"]));
  });

  it("keeps edits made after the seed (edit mode) so the form carries the new selection", async () => {
    const user = userEvent.setup();
    renderWithProviders(<EditHarness resetTo={["data.id", "data.name"]} />);

    await screen.findByDisplayValue("name");
    await waitFor(() => expect(paths().sort()).toEqual(["data.id", "data.name"]));

    const nameRow = rowOf("name");
    await user.click(within(nameRow).getByRole("checkbox"));

    await waitFor(() => expect(paths()).toEqual(["data.id"]));
    // and it must not snap back
    await new Promise((r) => setTimeout(r, 50));
    expect(paths()).toEqual(["data.id"]);
  });

  it("skeleton edits after an edit-mode seed reach the form and stick", async () => {
    const { container } = renderWithProviders(
      <EditHarness resetTo={["data.id", "data.name"]} />,
    );
    await screen.findByDisplayValue("data");
    await waitFor(() => expect(paths().sort()).toEqual(["data.id", "data.name"]));

    const editor = container.querySelector("textarea")!;
    fireEvent.change(editor, {
      target: { value: '{"data":{"id":null},"total":null}' },
    });

    await waitFor(() => expect(paths().sort()).toEqual(["data.id", "total"]));
    await new Promise((r) => setTimeout(r, 50));
    expect(paths().sort()).toEqual(["data.id", "total"]);
  });

  it("the skeleton editor rebuilds the tree from key structure only", async () => {
    const { container } = renderWithProviders(<Harness responseInclude={["data.id"]} />);

    const editor = container.querySelector("textarea")!;
    fireEvent.change(editor, {
      target: { value: '{"data":{"id":123,"note":"x"}}' },
    });

    await waitFor(() => expect(paths().sort()).toEqual(["data.id", "data.note"]));
  });
});

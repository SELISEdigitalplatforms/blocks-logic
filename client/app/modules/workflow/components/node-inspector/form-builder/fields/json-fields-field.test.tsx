import React from "react";
import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import type {
  FieldSchema,
  JsonFieldGroup,
  JsonFieldRow,
  SearchOptionsPage,
  SearchOptionsRequest,
} from "../form-field.types";
import { FIELD_COMPONENTS_REGISTRY } from "./fields";
import { JsonFieldsField } from "./json-fields-field";
import {
  REMOTE_SEARCH_DEBOUNCE_MS,
  REMOTE_SEARCH_PAGE_SIZE,
  REMOTE_SEARCH_SCROLL_THRESHOLD_PX,
} from "./remote-search.constants";

vi.mock("@/services/secret.service", async () => {
  const actual = await import("@/services/secret.service");
  const { mockSecretService } = await import("@/modules/proxy/test-support/mock-secret-service");
  return { ...actual, secretService: mockSecretService };
});

// Render counter (§5): counts each value input's expression-highlighter renders by its aria-label.
const highlighterRenders = vi.hoisted(() => new Map<string, number>());
vi.mock("../utils/expression-highlighter", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../utils/expression-highlighter")>();
  const Counting = (props: React.ComponentProps<typeof actual.ExpressionHighlighter>) => {
    const name = String((props.children.props as Record<string, unknown>)["aria-label"] ?? "");
    highlighterRenders.set(name, (highlighterRenders.get(name) ?? 0) + 1);
    return <actual.ExpressionHighlighter {...props} />;
  };
  return { ...actual, ExpressionHighlighter: Counting };
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { tenantId: "pk", workflowId: "wf", nodeId: "n1" };

const REMOTE_TOTAL = 35;
const remoteItems = Array.from({ length: REMOTE_TOTAL }, (_, i) => ({ value: `r${i}`, label: `Remote ${i}` }));

/** A stub returning 35 items in pages of 20. */
const pagedLoader = () =>
  vi.fn(async (req: SearchOptionsRequest): Promise<SearchOptionsPage> => {
    const start = req.page * req.pageSize;
    return { options: remoteItems.slice(start, start + req.pageSize), totalCount: REMOTE_TOTAL };
  });

type Loader = (req: SearchOptionsRequest) => Promise<SearchOptionsPage>;

const buildRows =
  (searchOptions: Loader = pagedLoader()) =>
  (): Array<JsonFieldRow | JsonFieldGroup> => [
    { key: "organizationId", kind: "text", defaultValue: "", required: true },
    {
      key: "email",
      kind: "text",
      defaultValue: "",
      required: true,
      info: "The user's email",
    },
    {
      key: "roles",
      kind: "list",
      defaultValue: [],
      allowCustom: true,
      options: [
        { value: "editor", label: "Editor" },
        { value: "viewer", label: "Viewer" },
      ],
    },
    { key: "remoteRoles", kind: "list", defaultValue: [], allowCustom: true, searchOptions },
    {
      group: "paging",
      info: "Page settings",
      rows: [
        { key: "page", kind: "int", defaultValue: 0 },
        { key: "pageSize", kind: "int", defaultValue: 10 },
      ],
    },
    { key: "notifyUser", kind: "bool", defaultValue: true },
    {
      key: "roleMode",
      kind: "enum",
      defaultValue: "add",
      options: [
        { value: "add", label: "Add", description: "Keep the existing roles" },
        { value: "replace", label: "Replace" },
      ],
    },
    { key: "userName", kind: "text", defaultValue: "", optional: true },
    { key: "phoneNumber", kind: "text", defaultValue: "", optional: true },
  ];

const makeField = (extra: Partial<FieldSchema> = {}): FieldSchema => ({
  id: "ops",
  key: "fields",
  type: "json-fields",
  label: "Fields",
  transient: true,
  rows: buildRows(),
  ...extra,
});

/** Holds the node data and applies each patch, as the form builder does. */
const Harness = ({
  field,
  initial,
  spy,
  readOnly,
}: {
  field: FieldSchema;
  initial: Record<string, unknown>;
  spy: (patch: Record<string, unknown>) => void;
  readOnly?: boolean;
}) => {
  const [data, setData] = React.useState(initial);
  return (
    <JsonFieldsField
      field={field}
      value={undefined}
      data={data}
      config={cfg}
      readOnly={readOnly}
      onChange={(patch) => {
        spy(patch as Record<string, unknown>);
        setData((prev) => ({ ...prev, ...(patch as Record<string, unknown>) }));
      }}
    />
  );
};

const setup = (opts: { field?: FieldSchema; initial?: Record<string, unknown>; readOnly?: boolean } = {}) => {
  const spy = vi.fn();
  const view = renderWithProviders(
    <Harness field={opts.field ?? makeField()} initial={opts.initial ?? {}} spy={spy} readOnly={opts.readOnly} />,
  );
  return { spy, ...view };
};

const block = () => screen.getByRole("group", { name: "Fields" });
const lineElements = () => Array.from(block().querySelectorAll("[data-json-line]")) as HTMLElement[];
const lineOf = (key: string) => screen.getByTestId(`json-row-${key}`).closest("[data-json-line]") as HTMLElement;
const rowOf = (key: string) => screen.getByTestId(`json-row-${key}`);
const textInput = (label: string) => screen.getByRole("textbox", { name: label }) as HTMLTextAreaElement;
const fxButton = (label: string) => screen.getByRole("button", { name: `Use an expression for ${label}` });

const transfer = (types: string[], text = "") => ({
  types,
  getData: (type: string) => (type === "text/plain" ? text : ""),
  dropEffect: "none",
});

afterEach(() => {
  vi.useRealTimers();
  highlighterRenders.clear();
});

describe("remote-search constants", () => {
  // C8 (D5): conditional-multiselect now imports these; its values are unchanged.
  it("keeps the shared paging rules", () => {
    expect(REMOTE_SEARCH_PAGE_SIZE).toBe(20);
    expect(REMOTE_SEARCH_DEBOUNCE_MS).toBe(500);
    expect(REMOTE_SEARCH_SCROLL_THRESHOLD_PX).toBe(32);
  });
});

describe("JsonFieldsField", () => {
  it("H1: renders through the registry as a numbered block with braces, a nested group and commas", () => {
    expect(FIELD_COMPONENTS_REGISTRY["json-fields"]).toBe(JsonFieldsField);
    setup();
    const lines = lineElements();
    expect(lines).toHaveLength(13);
    expect(lines.map((line) => line.firstElementChild?.textContent)).toEqual(
      Array.from({ length: 13 }, (_, i) => String(i + 1)),
    );
    expect(lines[0].textContent).toBe("1{");
    expect(lines[12].textContent).toBe("13}");
    expect(lines[5].textContent).toContain('"paging":{');
    expect(lines[8].textContent).toBe("9},");
    expect(lines[11].textContent).toContain("// add:");

    const hasComma = (key: string) => within(lineOf(key)).queryByText(",") !== null;
    for (const key of ["organizationId", "email", "roles", "remoteRoles", "page", "notifyUser"]) {
      expect(hasComma(key)).toBe(true);
    }
    // The last row of the group and of the block have none.
    expect(hasComma("pageSize")).toBe(false);
    expect(hasComma("roleMode")).toBe(false);
    // Rows are indented one level, group rows two.
    expect(lineOf("email").lastElementChild?.className).toContain("pl-[18px]");
    expect(lineOf("page").lastElementChild?.className).toContain("pl-9");
    // The key carries the required marker; values use the JSON text colour.
    const key = within(rowOf("email")).getByText("\"email\"");
    expect(key?.className).toContain("after:content-['*']");
    expect(block().className).toContain("font-mono");
  });

  it("H2: stores typed and dropped text exactly, with the variable picker available", () => {
    const { spy } = setup();
    fireEvent.change(textInput("email"), { target: { value: "{{ $json.email }} (work)" } });
    expect(spy).toHaveBeenLastCalledWith({ email: "{{ $json.email }} (work)" });
    expect(within(rowOf("email")).getByRole("button", { name: /insert a configuration variable/i })).toBeTruthy();
    expect(textInput("email").placeholder).toBe("required");
    expect(textInput("email").className).toContain("placeholder:text-red-500/70");

    // A drop elsewhere on the row appends with a space; a drop on the input is left to the browser.
    fireEvent.drop(rowOf("email"), { dataTransfer: transfer(["text/plain"], "{{ $json.extra }}") });
    expect(spy).toHaveBeenLastCalledWith({ email: "{{ $json.email }} (work) {{ $json.extra }}" });
    spy.mockClear();
    fireEvent.drop(textInput("organizationId"), { dataTransfer: transfer(["text/plain"], "{{ $json.orgId }}") });
    expect(spy).not.toHaveBeenCalled();
    fireEvent.drop(rowOf("organizationId"), { dataTransfer: transfer(["text/plain"], "{{ $json.orgId }}") });
    expect(spy).toHaveBeenLastCalledWith({ organizationId: "{{ $json.orgId }}" });
  });

  it("H2: highlights a row while text is dragged over it", () => {
    setup();
    fireEvent.dragOver(rowOf("email"), { dataTransfer: transfer(["text/plain"], "x") });
    expect(rowOf("email").className).toContain("bg-primary/10");
    fireEvent.dragLeave(rowOf("email"), { relatedTarget: document.body });
    expect(rowOf("email").className).not.toContain("bg-primary/10");
  });

  it("C11: keeps quotes, backslashes, pasted newlines and long text unchanged on one line", () => {
    const { spy } = setup();
    const value = `say "hi" \\ there\nsecond line ${"x".repeat(400)}`;
    fireEvent.change(textInput("organizationId"), { target: { value } });
    expect(spy).toHaveBeenLastCalledWith({ organizationId: value });
    const input = textInput("organizationId");
    expect(input.value).toBe(value);
    expect(input.getAttribute("wrap")).toBe("off");
    expect(input.rows).toBe(1);
    // The input grows with its text; the whole block scrolls sideways instead of the input.
    expect(input.className).toContain("overflow-hidden");
    expect(input.className).toContain("whitespace-pre");
    expect(block().firstElementChild?.className).toContain("overflow-x-auto");
  });

  it("H3: picks options, adds custom text and removes chips, in order and without duplicates", async () => {
    const user = userEvent.setup();
    const { spy } = setup();
    await user.click(screen.getByRole("button", { name: "Add a value to roles" }));
    await user.click(await screen.findByRole("option", { name: "Editor" }));
    expect(spy).toHaveBeenLastCalledWith({ roles: ["editor"] });
    // The popover stays open for more picks.
    await user.click(screen.getByRole("option", { name: "Viewer" }));
    expect(spy).toHaveBeenLastCalledWith({ roles: ["editor", "viewer"] });

    const search = screen.getByPlaceholderText("Search…");
    await user.type(search, "billing-x{Enter}");
    expect(spy).toHaveBeenLastCalledWith({ roles: ["editor", "viewer", "billing-x"] });
    spy.mockClear();
    await user.type(search, "billing-x{Enter}");
    expect(spy).not.toHaveBeenCalled();

    await user.keyboard("{Escape}");
    await user.click(screen.getByRole("button", { name: "Remove Editor" }));
    expect(spy).toHaveBeenLastCalledWith({ roles: ["viewer", "billing-x"] });
    // Chips show the option label, else the raw value.
    expect(within(rowOf("roles")).getByText("Viewer")).toBeTruthy();
    expect(within(rowOf("roles")).getByText("billing-x")).toBeTruthy();
  });

  it("H3: filters static options and shows 'No results.' when nothing matches", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("button", { name: "Add a value to roles" }));
    await user.type(screen.getByPlaceholderText("Search…"), "vie");
    expect(screen.queryByRole("option", { name: "Editor" })).toBeNull();
    expect(screen.getByRole("option", { name: "Viewer" })).toBeTruthy();
    await user.type(screen.getByPlaceholderText("Search…"), "zzz");
    expect(screen.getByText("No results.")).toBeTruthy();
  });

  it("H4: loads nothing until opened, then searches once per 500 ms pause and pages on scroll", async () => {
    vi.useFakeTimers();
    const loader = pagedLoader();
    setup({ field: makeField({ rows: buildRows(loader) }) });
    expect(loader).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Add a value to remoteRoles" }));
    await act(async () => {});
    expect(loader).toHaveBeenCalledTimes(1);
    expect(loader.mock.calls[0][0]).toEqual({ search: "", page: 0, pageSize: 20 });
    expect(screen.getAllByRole("option")).toHaveLength(20);

    const search = screen.getByPlaceholderText("Search…");
    fireEvent.change(search, { target: { value: " R" } });
    fireEvent.change(search, { target: { value: " Re " } });
    await act(async () => vi.advanceTimersByTime(REMOTE_SEARCH_DEBOUNCE_MS - 1));
    expect(loader).toHaveBeenCalledTimes(1);
    await act(async () => vi.advanceTimersByTime(1));
    expect(loader).toHaveBeenCalledTimes(2);
    expect(loader.mock.calls[1][0]).toEqual({ search: "Re", page: 0, pageSize: 20 });

    const list = document.querySelector("[cmdk-list]") as HTMLElement;
    Object.defineProperty(list, "scrollHeight", { value: 1000, configurable: true });
    Object.defineProperty(list, "clientHeight", { value: 200, configurable: true });
    // Further than 32 px from the bottom: no page.
    list.scrollTop = 760;
    fireEvent.scroll(list);
    expect(loader).toHaveBeenCalledTimes(2);
    list.scrollTop = 770;
    fireEvent.scroll(list);
    await act(async () => {});
    expect(loader).toHaveBeenCalledTimes(3);
    expect(loader.mock.calls[2][0]).toEqual({ search: "Re", page: 1, pageSize: 20 });
    expect(screen.getAllByRole("option")).toHaveLength(REMOTE_TOTAL);
    // 35 of 35 loaded: no more pages.
    fireEvent.scroll(list);
    await act(async () => {});
    expect(loader).toHaveBeenCalledTimes(3);
  });

  it("C3: drops an older search's late response and never loads two pages at once", async () => {
    vi.useFakeTimers();
    const pending: Array<{ req: SearchOptionsRequest; resolve: (page: SearchOptionsPage) => void }> = [];
    const loader = vi.fn(
      (req: SearchOptionsRequest) =>
        new Promise<SearchOptionsPage>((resolve) => {
          pending.push({ req, resolve });
        }),
    );
    setup({ field: makeField({ rows: buildRows(loader) }) });
    fireEvent.click(screen.getByRole("button", { name: "Add a value to remoteRoles" }));
    await act(async () => {});
    const page = (prefix: string, n: number) => ({
      options: Array.from({ length: n }, (_, i) => ({ value: `${prefix}${i}`, label: `${prefix} ${i}` })),
      totalCount: 100,
    });

    const search = screen.getByPlaceholderText("Search…");
    fireEvent.change(search, { target: { value: "old" } });
    await act(async () => vi.advanceTimersByTime(REMOTE_SEARCH_DEBOUNCE_MS));
    fireEvent.change(search, { target: { value: "new" } });
    await act(async () => vi.advanceTimersByTime(REMOTE_SEARCH_DEBOUNCE_MS));
    expect(pending.map((p) => p.req.search)).toEqual(["", "old", "new"]);

    await act(async () => pending[2].resolve(page("new", 20)));
    await act(async () => pending[1].resolve(page("old", 20)));
    await act(async () => pending[0].resolve(page("first", 20)));
    expect(screen.getByRole("option", { name: "new 0" })).toBeTruthy();
    expect(screen.queryByRole("option", { name: "old 0" })).toBeNull();
    expect(screen.queryByRole("option", { name: "first 0" })).toBeNull();

    const list = document.querySelector("[cmdk-list]") as HTMLElement;
    Object.defineProperty(list, "scrollHeight", { value: 1000, configurable: true });
    Object.defineProperty(list, "clientHeight", { value: 1000, configurable: true });
    fireEvent.scroll(list);
    fireEvent.scroll(list);
    await act(async () => {});
    expect(loader).toHaveBeenCalledTimes(4);
    expect(pending[3].req).toEqual({ search: "new", page: 1, pageSize: 20 });
  });

  it("C2: a failed first load shows 'No results.' and keeps the value; a failed later page keeps the pages", async () => {
    vi.useFakeTimers();
    let failFirst = true;
    const loader = vi.fn(async (req: SearchOptionsRequest): Promise<SearchOptionsPage> => {
      if (req.page === 0 && failFirst) throw new Error("IAM down");
      if (req.page > 0) throw new Error("page failed");
      return { options: remoteItems.slice(0, 20), totalCount: REMOTE_TOTAL };
    });
    const { spy } = setup({ field: makeField({ rows: buildRows(loader) }), initial: { remoteRoles: ["kept"] } });
    const trigger = screen.getByRole("button", { name: "Add a value to remoteRoles" });
    fireEvent.click(trigger);
    await act(async () => {});
    expect(screen.getByText("No results.")).toBeTruthy();
    expect(document.querySelector(".animate-spin")).toBeNull();
    expect(within(rowOf("remoteRoles")).getByText("kept")).toBeTruthy();

    // The next opening tries again.
    failFirst = false;
    fireEvent.keyDown(document.activeElement ?? document.body, { key: "Escape" });
    await act(async () => {});
    fireEvent.click(trigger);
    await act(async () => {});
    expect(screen.getAllByRole("option")).toHaveLength(20);

    const list = document.querySelector("[cmdk-list]") as HTMLElement;
    Object.defineProperty(list, "scrollHeight", { value: 100, configurable: true });
    Object.defineProperty(list, "clientHeight", { value: 100, configurable: true });
    fireEvent.scroll(list);
    await act(async () => {});
    expect(screen.getAllByRole("option")).toHaveLength(20);
    const calls = loader.mock.calls.length;
    fireEvent.scroll(list);
    await act(async () => {});
    expect(loader.mock.calls.length).toBe(calls);
    expect(spy).not.toHaveBeenCalled();
  });

  it("clears a list picker's loaded pages when an optionsDependencies key changes", async () => {
    const loader = vi.fn(async (_req: SearchOptionsRequest, data: Record<string, unknown>) => ({
      options: [{ value: `${data.organizationId}-role`, label: `Role of ${data.organizationId || "none"}` }],
      totalCount: 1,
    }));
    const rows = (): JsonFieldRow[] => [
      { key: "organizationId", kind: "text", defaultValue: "" },
      { key: "roles", kind: "list", defaultValue: [], searchOptions: loader, optionsDependencies: ["organizationId"] },
    ];
    const user = userEvent.setup();
    setup({ field: makeField({ rows }), initial: { organizationId: "org1" } });
    await user.click(screen.getByRole("button", { name: "Add a value to roles" }));
    expect(await screen.findByRole("option", { name: "Role of org1" })).toBeTruthy();
    await user.keyboard("{Escape}");

    fireEvent.change(textInput("organizationId"), { target: { value: "org2" } });
    await user.click(screen.getByRole("button", { name: "Add a value to roles" }));
    expect(await screen.findByRole("option", { name: "Role of org2" })).toBeTruthy();
    expect(screen.queryByRole("option", { name: "Role of org1" })).toBeNull();
    expect(loader).toHaveBeenCalledTimes(2);
  });

  it("H5: flips a bool by click and Space, stores a typed number and an enum option", async () => {
    const user = userEvent.setup();
    const { spy } = setup();
    const toggle = screen.getByRole("switch", { name: "notifyUser" });
    expect(toggle.textContent).toBe("true");
    await user.click(toggle);
    expect(spy).toHaveBeenLastCalledWith({ notifyUser: false });
    toggle.focus();
    await user.keyboard(" ");
    expect(spy).toHaveBeenLastCalledWith({ notifyUser: true });
    await user.keyboard("{Enter}");
    expect(spy).toHaveBeenLastCalledWith({ notifyUser: false });

    fireEvent.change(screen.getByRole("textbox", { name: "page" }), { target: { value: "3" } });
    expect(spy).toHaveBeenLastCalledWith({ page: 3 });

    await user.click(screen.getByRole("button", { name: "roleMode" }));
    expect(screen.getByText("Keep the existing roles")).toBeTruthy();
    await user.click(screen.getByRole("option", { name: /Replace/ }));
    expect(spy).toHaveBeenLastCalledWith({ roleMode: "replace" });
    expect(screen.queryByRole("option", { name: /Replace/ })).toBeNull();
  });

  it("D8: an int input writes nothing while empty or partial, and the default on blur", () => {
    const { spy } = setup({ initial: { pageSize: 25 } });
    const input = screen.getByRole("textbox", { name: "pageSize" }) as HTMLInputElement;
    fireEvent.change(input, { target: { value: "" } });
    fireEvent.change(input, { target: { value: "-" } });
    expect(spy).not.toHaveBeenCalled();
    fireEvent.change(input, { target: { value: "" } });
    fireEvent.blur(input);
    expect(spy).toHaveBeenLastCalledWith({ pageSize: 10 });
    expect(input.value).toBe("10");
  });

  it("H6: fx toggles a row to an expression and back to its default; drops and {{ switch to fx", async () => {
    const user = userEvent.setup();
    const { spy } = setup();
    const fx = fxButton("notifyUser");
    expect(fx.getAttribute("aria-pressed")).toBe("false");
    await user.click(fx);
    expect(spy).toHaveBeenLastCalledWith({ notifyUser: "" });
    expect(fxButton("notifyUser").getAttribute("aria-pressed")).toBe("true");
    expect(textInput("notifyUser").placeholder).toBe("{{ expression }}");
    expect(textInput("notifyUser").closest(".bg-primary\\/10")).toBeTruthy();
    fireEvent.change(textInput("notifyUser"), { target: { value: "{{ $json.notify }}" } });
    expect(spy).toHaveBeenLastCalledWith({ notifyUser: "{{ $json.notify }}" });
    await user.click(fxButton("notifyUser"));
    expect(spy).toHaveBeenLastCalledWith({ notifyUser: true });
    expect(screen.getByRole("switch", { name: "notifyUser" })).toBeTruthy();

    // Example 2: list fx on, a drop, fx off.
    await user.click(fxButton("roles"));
    expect(spy).toHaveBeenLastCalledWith({ roles: "" });
    fireEvent.drop(rowOf("roles"), { dataTransfer: transfer(["text/plain"], "{{ $json.roles }}") });
    expect(spy).toHaveBeenLastCalledWith({ roles: "{{ $json.roles }}" });
    await user.click(fxButton("roles"));
    expect(spy).toHaveBeenLastCalledWith({ roles: [] });

    // A drop on an int row replaces its value with the expression.
    fireEvent.drop(rowOf("pageSize"), { dataTransfer: transfer(["text/plain"], "{{ $json.size }}") });
    expect(spy).toHaveBeenLastCalledWith({ pageSize: "{{ $json.size }}" });
    expect(textInput("pageSize").value).toBe("{{ $json.size }}");

    // Example 3: typing {{ in an int row carries the text into fx.
    const page = screen.getByRole("textbox", { name: "page" });
    page.focus();
    fireEvent.change(page, { target: { value: "{{" } });
    expect(spy).toHaveBeenLastCalledWith({ page: "{{" });
    expect(textInput("page").value).toBe("{{");
    expect(fxButton("page").getAttribute("aria-pressed")).toBe("true");
    expect(document.activeElement).toBe(textInput("page"));
  });

  it("H7: string values of list, bool and int rows and non-option enum strings open in fx", () => {
    setup({
      initial: { roles: "{{ $json.roles }}", notifyUser: "{{ $json.n }}", page: "{{ $json.p }}", roleMode: "" },
    });
    for (const key of ["roles", "notifyUser", "page", "roleMode"]) {
      expect(fxButton(key).getAttribute("aria-pressed")).toBe("true");
    }
    expect(textInput("roles").value).toBe("{{ $json.roles }}");
    expect(textInput("roleMode").value).toBe("");
    // Text rows have no fx toggle.
    expect(screen.queryByRole("button", { name: "Use an expression for email" })).toBeNull();
  });

  it("H8: lists empty optional rows on // add:, shows and focuses one, and × hides it again", async () => {
    const user = userEvent.setup();
    const { spy } = setup({ initial: { phoneNumber: "+100" } });
    // A stored optional value shows its row.
    expect(rowOf("phoneNumber")).toBeTruthy();
    const addLine = lineElements()[lineElements().length - 2];
    expect(within(addLine).getByRole("button", { name: "userName" })).toBeTruthy();
    expect(within(addLine).queryByRole("button", { name: "phoneNumber" })).toBeNull();

    await user.click(within(addLine).getByRole("button", { name: "userName" }));
    expect(document.activeElement).toBe(textInput("userName"));
    expect(spy).not.toHaveBeenCalled();
    await user.keyboard("ana");
    expect(spy).toHaveBeenLastCalledWith({ userName: "ana" });

    await user.click(screen.getByRole("button", { name: "Remove userName" }));
    expect(spy).toHaveBeenLastCalledWith({ userName: "" });
    expect(screen.queryByTestId("json-row-userName")).toBeNull();
    // Keyboard focus moves to the row's // add: button.
    expect(document.activeElement?.textContent).toBe("userName");
    await user.click(screen.getByRole("button", { name: "Remove phoneNumber" }));
    expect(spy).toHaveBeenLastCalledWith({ phoneNumber: "" });
    const finalAdd = lineElements()[lineElements().length - 2];
    expect(within(finalAdd).getAllByRole("button").map((b) => b.textContent)).toEqual(["userName", "phoneNumber"]);
  });

  it("every row that is not required has ×, which moves it to // add: until added again", async () => {
    const user = userEvent.setup();
    const { spy } = setup({ initial: { notifyUser: false } });
    expect(screen.queryByRole("button", { name: "Remove email" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Remove organizationId" })).toBeNull();
    await user.click(screen.getByRole("button", { name: "Remove notifyUser" }));
    expect(spy).toHaveBeenLastCalledWith({ notifyUser: true });
    expect(screen.queryByTestId("json-row-notifyUser")).toBeNull();
    expect(document.activeElement?.textContent).toBe("notifyUser");
    await user.click(screen.getByRole("button", { name: "notifyUser" }));
    expect(screen.getByRole("switch", { name: "notifyUser" })).toBeTruthy();
  });

  it("Example 7: one edit writes exactly one patch of that key", async () => {
    const user = userEvent.setup();
    const { spy } = setup();
    await user.click(screen.getByRole("button", { name: "Add a value to roles" }));
    await user.click(await screen.findByRole("option", { name: "Editor" }));
    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy.mock.calls[0][0]).toEqual({ roles: ["editor"] });
  });

  it("§5 / D2: typing in one row re-renders no other row", () => {
    setup();
    fireEvent.change(textInput("email"), { target: { value: "a" } });
    highlighterRenders.clear();
    fireEvent.change(textInput("email"), { target: { value: "ab" } });
    fireEvent.change(textInput("email"), { target: { value: "abc" } });
    expect(highlighterRenders.get("email")).toBeGreaterThanOrEqual(2);
    expect(highlighterRenders.get("organizationId") ?? 0).toBe(0);
  });

  it("C4: shows defaults for missing and null values without writing them", async () => {
    const { spy } = setup({ initial: { notifyUser: null, page: null, roles: null } });
    await act(async () => {});
    expect(screen.getByRole("switch", { name: "notifyUser" }).textContent).toBe("true");
    expect((screen.getByRole("textbox", { name: "page" }) as HTMLInputElement).value).toBe("0");
    expect((screen.getByRole("textbox", { name: "pageSize" }) as HTMLInputElement).value).toBe("10");
    expect(screen.getByRole("button", { name: "roleMode" }).textContent).toContain('"add"');
    expect(screen.getByRole("button", { name: "Add a value to roles" }).textContent).toBe("+ add");
    expect(spy).not.toHaveBeenCalled();
  });

  it("C5: read-only disables every control and never writes", async () => {
    const user = userEvent.setup();
    const { spy } = setup({ readOnly: true, initial: { roles: ["editor"] } });
    const controls = within(block()).getAllByRole("button");
    for (const control of controls) {
      expect((control as HTMLButtonElement).disabled).toBe(true);
    }
    for (const input of within(block()).getAllByRole("textbox")) {
      expect((input as HTMLInputElement).disabled).toBe(true);
    }
    expect(within(block()).getByRole("switch", { name: "notifyUser" })).toHaveProperty("disabled", true);
    expect(screen.queryByRole("button", { name: /insert a configuration variable/i })).toBeNull();

    await user.click(screen.getByRole("button", { name: "Add a value to roles" }));
    expect(screen.queryByRole("option")).toBeNull();
    fireEvent.dragOver(rowOf("email"), { dataTransfer: transfer(["text/plain"], "x") });
    expect(rowOf("email").className).not.toContain("bg-primary/10");
    fireEvent.drop(rowOf("email"), { dataTransfer: transfer(["text/plain"], "x") });
    fireEvent.drop(rowOf("page"), { dataTransfer: transfer(["text/plain"], "x") });
    expect(spy).not.toHaveBeenCalled();
  });

  it("C5: a disabled field behaves as read-only", () => {
    const { spy } = setup({ field: makeField({ disabled: true }) });
    expect(textInput("email").disabled).toBe(true);
    fireEvent.click(screen.getByRole("switch", { name: "notifyUser" }));
    expect(spy).not.toHaveBeenCalled();
  });

  it("C6: renders rows with {} when rowsContext rejects, and never retries in the mount", async () => {
    const rowsContext = vi.fn().mockRejectedValue(new Error("config down"));
    const rows = vi.fn((_data: Record<string, unknown>, context: Record<string, unknown>): JsonFieldRow[] => [
      { key: "organizationId", kind: "text", defaultValue: "", required: context.multi === true },
    ]);
    setup({ field: makeField({ rows, rowsContext }) });
    await act(async () => {});
    expect(rowsContext).toHaveBeenCalledTimes(1);
    expect(rowsContext).toHaveBeenCalledWith(cfg);
    expect(rows).toHaveBeenLastCalledWith(expect.anything(), {});
    fireEvent.change(textInput("organizationId"), { target: { value: "x" } });
    await act(async () => {});
    expect(rowsContext).toHaveBeenCalledTimes(1);
    expect(textInput("organizationId")).toBeTruthy();
  });

  it("C6: uses {} while rowsContext is slow, then the resolved context", async () => {
    let resolve: (value: Record<string, unknown>) => void = () => {};
    const rowsContext = vi.fn(() => new Promise<Record<string, unknown>>((r) => (resolve = r)));
    const rows = (_data: Record<string, unknown>, context: Record<string, unknown>): JsonFieldRow[] => [
      { key: "organizationId", kind: "text", defaultValue: "", required: context.multi === true },
    ];
    setup({ field: makeField({ rows, rowsContext }) });
    expect(textInput("organizationId").placeholder).toBe("empty");
    await waitFor(() => expect(rowsContext).toHaveBeenCalledTimes(1));
    expect(textInput("organizationId").placeholder).toBe("empty");
    await act(async () => resolve({ multi: true }));
    await waitFor(() => expect(textInput("organizationId").placeholder).toBe("required"));
    expect(rowsContext).toHaveBeenCalledTimes(1);
  });

  it("C7: ignores dropped files and HTML-only drags", () => {
    const { spy } = setup();
    for (const key of ["email", "page", "roles"]) {
      fireEvent.dragOver(rowOf(key), { dataTransfer: transfer(["Files"]) });
      expect(rowOf(key).className).not.toContain("bg-primary/10");
      fireEvent.drop(rowOf(key), { dataTransfer: transfer(["Files"]) });
      fireEvent.drop(rowOf(key), { dataTransfer: transfer(["text/html"]) });
    }
    fireEvent.drop(textInput("email"), { dataTransfer: transfer(["Files"]) });
    expect(spy).not.toHaveBeenCalled();
  });

  it("H13: reaches every control with Tab in line order, and Escape returns focus to the opener", async () => {
    const user = userEvent.setup();
    const { spy } = setup({ field: makeField({ variablePicker: false }) });
    textInput("organizationId").focus();
    const order: string[] = [];
    for (let i = 0; i < 22; i++) {
      const el = document.activeElement as HTMLElement;
      order.push(el.getAttribute("aria-label") ?? el.textContent ?? "");
      await user.tab();
    }
    expect(order).toEqual([
      "organizationId",
      "email",
      "Add a value to roles",
      "Use an expression for roles",
      "Remove roles",
      "Add a value to remoteRoles",
      "Use an expression for remoteRoles",
      "Remove remoteRoles",
      "page",
      "Use an expression for page",
      "Remove page",
      "pageSize",
      "Use an expression for pageSize",
      "Remove pageSize",
      "notifyUser",
      "Use an expression for notifyUser",
      "Remove notifyUser",
      "roleMode",
      "Use an expression for roleMode",
      "Remove roleMode",
      "userName",
      "phoneNumber",
    ]);

    const enumTrigger = screen.getByRole("button", { name: "roleMode" });
    enumTrigger.focus();
    await user.keyboard("{Enter}");
    expect(await screen.findByRole("option", { name: /Replace/ })).toBeTruthy();
    await user.keyboard("{ArrowDown}{Enter}");
    expect(spy).toHaveBeenLastCalledWith({ roleMode: "replace" });
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("button", { name: "roleMode" })));

    const listTrigger = screen.getByRole("button", { name: "Add a value to roles" });
    listTrigger.focus();
    await user.keyboard("{Enter}");
    await screen.findByRole("option", { name: "Editor" });
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("option", { name: "Editor" })).toBeNull();
    await waitFor(() => expect(document.activeElement).toBe(listTrigger));
  });

  it("H8: an optional row stays shown while edited, also when it turns empty or switches to fx", async () => {
    const user = userEvent.setup();
    const rows = (): JsonFieldRow[] => [
      { key: "userName", kind: "text", defaultValue: "", optional: true },
      { key: "limit", kind: "int", defaultValue: 0, optional: true },
    ];
    const { spy } = setup({ field: makeField({ rows }), initial: { userName: "ana", limit: 5 } });
    fireEvent.change(textInput("userName"), { target: { value: "" } });
    expect(spy).toHaveBeenLastCalledWith({ userName: "" });
    expect(rowOf("userName")).toBeTruthy();

    await user.click(fxButton("limit"));
    expect(spy).toHaveBeenLastCalledWith({ limit: "" });
    expect(textInput("limit")).toBeTruthy();
    expect(document.activeElement).toBe(textInput("limit"));
    await user.click(screen.getByRole("button", { name: "Remove limit" }));
    expect(spy).toHaveBeenLastCalledWith({ limit: 0 });
    expect(screen.queryByTestId("json-row-limit")).toBeNull();
  });

  it("H3: Enter on text naming an option adds that option once", async () => {
    const user = userEvent.setup();
    const { spy } = setup({ initial: { roles: ["editor"] } });
    await user.click(screen.getByRole("button", { name: "Add a value to roles" }));
    const search = screen.getByPlaceholderText("Search…");
    await user.type(search, "viewer{Enter}");
    expect(spy).toHaveBeenLastCalledWith({ roles: ["editor", "viewer"] });
    spy.mockClear();
    // Already selected: Enter keeps it rather than toggling it off.
    await user.type(search, "Editor{Enter}");
    expect(spy).not.toHaveBeenCalled();
  });

  it("shows duplicate stored list values once, without writing", async () => {
    const { spy } = setup({ initial: { roles: ["editor", "editor", "x"] } });
    await act(async () => {});
    expect(within(rowOf("roles")).getAllByText("Editor")).toHaveLength(1);
    expect(spy).not.toHaveBeenCalled();
  });

  it("H2: highlights {{$...}} expressions in a text row", () => {
    setup({ initial: { email: "{{$json.input.email}} (work)" } });
    const overlay = rowOf("email").querySelector('[aria-hidden="true"]');
    const highlighted = Array.from(overlay?.querySelectorAll("span") ?? []).find(
      (span) => span.textContent === "{{$json.input.email}}",
    );
    expect(highlighted?.className).toMatch(/text-(green|red)-700/);
  });
});

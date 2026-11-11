import { act, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import type { FieldSchema, SelectOption } from "../form-field.types";
import { FIELD_COMPONENTS_REGISTRY } from "./fields";
import { SearchableSelectField } from "./searchable-select-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { tenantId: "pk", workflowId: "wf", nodeId: "n1" };

const OPTIONS: SelectOption[] = [
  { value: "cc-1", label: "Main" },
  { value: "cc-2", label: "Backup" },
  { value: "cc-3", label: "Archive" },
];

const makeField = (extra: Partial<FieldSchema> = {}): FieldSchema => ({
  id: "credential",
  key: "credentialId",
  type: "searchable-select",
  label: "Credential",
  options: OPTIONS,
  ...extra,
});

const renderField = (
  opts: { field?: FieldSchema; value?: unknown; data?: Record<string, unknown>; readOnly?: boolean } = {},
) => {
  const onChange = vi.fn();
  const props = {
    field: opts.field ?? makeField(),
    value: opts.value as string,
    onChange,
    data: opts.data ?? {},
    config: cfg,
    readOnly: opts.readOnly,
  };
  const view = renderWithProviders(<SearchableSelectField {...props} />);
  const rerender = (next: Partial<typeof props>) =>
    view.rerender(<SearchableSelectField {...props} {...next} />);
  return { ...view, onChange, rerender };
};

const trigger = () => screen.getByRole("combobox");
const WARNING = "This item is not in the list. It may be inactive, deleted or from another project.";

describe("SearchableSelectField", () => {
  it("H11: renders with the select trigger styling, filters while typing and stores the selection", async () => {
    expect(FIELD_COMPONENTS_REGISTRY["searchable-select"]).toBe(SearchableSelectField);
    const user = userEvent.setup();
    const { onChange } = renderField({ value: "" });
    expect(trigger().textContent).toContain("Select an option");
    expect(trigger().className).toContain("h-10 w-full items-center justify-between rounded-md border border-input");
    expect(trigger().id).toBe("credential");

    await user.click(trigger());
    await user.type(screen.getByPlaceholderText("Search…"), "BACK");
    expect(screen.getAllByRole("option").map((o) => o.textContent)).toEqual(["Backup"]);
    await user.click(screen.getByRole("option", { name: "Backup" }));
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith("cc-2");
    expect(screen.queryByRole("option")).toBeNull();
  });

  it("shows the selected option's label and the placeholder for an empty value", () => {
    renderField({ value: "cc-3", field: makeField({ placeholder: "Pick a credential" }) });
    expect(trigger().textContent).toContain("Archive");
    expect(screen.queryByText(WARNING)).toBeNull();
  });

  it("C1 / Example 8: keeps an unmatched saved value, labels it and never writes", async () => {
    const user = userEvent.setup();
    const { onChange } = renderField({
      value: "cc-old",
      field: makeField({
        options: [{ value: "cc-1", label: "Main" }],
        unmatchedLabel: (v) => `Not found or inactive (${v})`,
      }),
    });
    expect(trigger().textContent).toContain("Not found or inactive (cc-old)");
    expect(screen.getByText(WARNING)).toBeTruthy();

    await user.click(trigger());
    const options = screen.getAllByRole("option");
    expect(options[0].textContent).toBe("Not found or inactive (cc-old)");
    await user.click(options[0]);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("C1: defaults the unmatched label to the raw value", () => {
    renderField({ value: "cc-gone" });
    expect(trigger().textContent).toContain("cc-gone");
    expect(screen.getByText(WARNING)).toBeTruthy();
  });

  it("loads async options once per mount and again when a dependency changes, never writing", async () => {
    const loader = vi.fn(async (data: Record<string, unknown>) => [
      { value: "cc-1", label: `Main of ${data.project}` },
    ]);
    const field = makeField({ options: loader, optionsDependencies: ["project"] });
    const { onChange, rerender } = renderField({ field, value: "cc-old", data: { project: "a" } });
    // No warning while the options are loading.
    expect(screen.queryByText(WARNING)).toBeNull();
    await waitFor(() => expect(screen.getByText(WARNING)).toBeTruthy());
    expect(loader).toHaveBeenCalledTimes(1);

    rerender({ data: { project: "a", other: 1 } });
    await act(async () => {});
    expect(loader).toHaveBeenCalledTimes(1);
    rerender({ data: { project: "b" }, value: "cc-1" });
    await waitFor(() => expect(trigger().textContent).toContain("Main of b"));
    expect(loader).toHaveBeenCalledTimes(2);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("loads async options once when no dependencies are declared", async () => {
    const loader = vi.fn().mockResolvedValue(OPTIONS);
    const { rerender } = renderField({ field: makeField({ options: loader }), value: "cc-1" });
    await waitFor(() => expect(trigger().textContent).toContain("Main"));
    rerender({ data: { anything: true } });
    await act(async () => {});
    expect(loader).toHaveBeenCalledTimes(1);
  });

  it("C2: a rejected load shows 'No results.', no warning, and keeps the value", async () => {
    const user = userEvent.setup();
    const loader = vi.fn().mockRejectedValue(new Error("down"));
    const { onChange } = renderField({ field: makeField({ options: loader }), value: "cc-1" });
    await act(async () => {});
    expect(trigger().textContent).toContain("cc-1");
    expect(screen.queryByText(WARNING)).toBeNull();
    expect(document.querySelector(".animate-spin")).toBeNull();
    await user.click(trigger());
    expect(screen.getByText("No results.")).toBeTruthy();
    expect(onChange).not.toHaveBeenCalled();
  });

  it("shows 'No results.' when the search matches nothing", async () => {
    const user = userEvent.setup();
    renderField({ value: "" });
    await user.click(trigger());
    await user.type(screen.getByPlaceholderText("Search…"), "zzz");
    expect(screen.getByText("No results.")).toBeTruthy();
  });

  it("C5: read-only and disabled fields keep the trigger disabled", async () => {
    const user = userEvent.setup();
    const { onChange } = renderField({ value: "cc-1", readOnly: true });
    expect(trigger()).toHaveProperty("disabled", true);
    await user.click(trigger());
    expect(screen.queryByRole("option")).toBeNull();
    expect(onChange).not.toHaveBeenCalled();
  });

  it("C5: a disabled field keeps the trigger disabled", () => {
    renderField({ value: "", field: makeField({ disabled: true }) });
    expect(trigger()).toHaveProperty("disabled", true);
  });

  it("H13: opens with Enter or Space, moves with arrows, selects with Enter, Escape returns focus", async () => {
    const user = userEvent.setup();
    const { onChange } = renderField({ value: "cc-1" });
    trigger().focus();
    await user.keyboard("{Enter}");
    expect(await screen.findByRole("option", { name: "Main" })).toBeTruthy();
    await user.keyboard("{ArrowDown}{Enter}");
    expect(onChange).toHaveBeenLastCalledWith("cc-2");
    await waitFor(() => expect(document.activeElement).toBe(trigger()));

    await user.keyboard(" ");
    expect(await screen.findByRole("option", { name: "Main" })).toBeTruthy();
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("option")).toBeNull();
    await waitFor(() => expect(document.activeElement).toBe(trigger()));
    expect(onChange).toHaveBeenCalledTimes(1);
  });
});

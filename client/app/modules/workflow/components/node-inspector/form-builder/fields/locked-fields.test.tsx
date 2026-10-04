import { describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { FieldSchema } from "../form-field.types";
import { withLockedKeys, withoutLockedKeys } from "../utils/json-locked-keys";
import { JsonCodeEditor } from "./json-code-editor-field";
import { KeyValuePairsField } from "./key-value-pairs-field";
import { SwitchField } from "./switch-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { tenantId: "pk", workflowId: "wf", nodeId: "n1" };

const field = (extra: Partial<FieldSchema>): FieldSchema => ({
  id: "f",
  key: "f",
  label: "Field",
  type: "text",
  lockedDependencies: ["routePath"],
  ...extra,
});

describe("json locked keys", () => {
  const locked = { tenant: "acme", key: "{{$VAR.api-key}}" };

  it("puts the locked keys in front of the saved body", () => {
    expect(JSON.parse(withLockedKeys('{"note": "hi"}', locked))).toEqual({
      tenant: "acme",
      key: "{{$VAR.api-key}}",
      note: "hi",
    });
    expect(JSON.parse(withLockedKeys("", locked))).toEqual(locked);
  });

  it("keeps expressions, bare or inside a string, through the round trip", () => {
    const text = withLockedKeys('{"id": {{$json.input.id}}, "who": "user {{$json.input.name}}"}', locked);
    expect(text).toContain('"id": {{$json.input.id}}');
    expect(text).toContain('"who": "user {{$json.input.name}}"');
    expect(withoutLockedKeys(text, locked)).toEqual({
      value: '{\n  "id": {{$json.input.id}},\n  "who": "user {{$json.input.name}}"\n}',
      error: undefined,
    });
  });

  it("strips the locked keys before saving and names any the user changed or removed", () => {
    const edited = '{"tenant": "other", "note": "hi"}';
    const result = withoutLockedKeys(edited, locked);
    expect(JSON.parse(result.value)).toEqual({ note: "hi" });
    expect(result.error).toBe(`"tenant", "key" are locked and can't be changed or removed.`);
  });

  it("leaves text that is not a JSON object as typed", () => {
    expect(withLockedKeys("{ broken", locked)).toBe("{ broken");
    expect(withoutLockedKeys("{ broken", locked)).toEqual({ value: "{ broken" });
  });
});

describe("locked values on form-builder fields", () => {
  it("forces a switch on, disabled, and saves it as on", async () => {
    const onChange = vi.fn();
    renderWithProviders(
      <SwitchField
        field={field({ type: "switch", locked: async () => true })}
        value={false}
        onChange={onChange}
        data={{ routePath: "a" }}
        config={cfg}
      />,
    );

    await waitFor(() => expect(onChange).toHaveBeenCalledWith(true));
    const toggle = screen.getByRole("switch");
    expect(toggle.getAttribute("aria-checked")).toBe("true");
    expect((toggle as HTMLButtonElement).disabled).toBe(true);
  });

  it("leaves a switch alone when its locked value is false", async () => {
    const onChange = vi.fn();
    const locked = vi.fn(async () => false);
    renderWithProviders(
      <SwitchField
        field={field({ type: "switch", locked })}
        value={false}
        onChange={onChange}
        data={{ routePath: "a" }}
        config={cfg}
      />,
    );

    await waitFor(() => expect(locked).toHaveBeenCalled());
    expect((screen.getByRole("switch") as HTMLButtonElement).disabled).toBe(false);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("shows locked key-value rows above the user's own, without saving them", async () => {
    const onChange = vi.fn();
    renderWithProviders(
      <KeyValuePairsField
        field={field({
          type: "key-value-pairs",
          label: "Query",
          locked: async () => ({ api_key: "abc" }),
        })}
        value={{ page: "2" }}
        onChange={onChange}
        data={{ routePath: "a" }}
        config={cfg}
      />,
    );

    const lockedKey = (await screen.findByDisplayValue("api_key")) as HTMLInputElement;
    const lockedValue = screen.getByDisplayValue("abc") as HTMLInputElement;
    expect(lockedKey.disabled).toBe(true);
    expect(lockedValue.disabled).toBe(true);
    expect(screen.getByLabelText("Locked")).toBeTruthy();
    // No secret picker on the locked row: only the user's row has them.
    expect(
      screen.queryAllByRole("button", { name: /insert a configuration variable/i }),
    ).toHaveLength(2);

    fireEvent.change(screen.getByDisplayValue("2"), { target: { value: "3" } });
    expect(onChange).toHaveBeenLastCalledWith({ page: "3" });
  });

  it("warns when a user row reuses a locked key", async () => {
    renderWithProviders(
      <KeyValuePairsField
        field={field({ type: "key-value-pairs", locked: async () => ({ api_key: "abc" }) })}
        value={{ api_key: "mine" }}
        onChange={vi.fn()}
        data={{ routePath: "a" }}
        config={cfg}
      />,
    );

    expect(await screen.findByText(/A locked row sets "api_key"; its value is sent instead./)).toBeTruthy();
  });

  it("prefills a JSON body with locked keys and saves only the user's", async () => {
    const onChange = vi.fn();
    renderWithProviders(
      <JsonCodeEditor
        field={field({ type: "json-code-editor", locked: async () => ({ tenant: "acme" }) })}
        value='{"note": "hi"}'
        onChange={onChange}
        data={{ routePath: "a" }}
        config={cfg}
      />,
    );

    const editor = (await screen.findByDisplayValue(/"tenant": "acme"/)) as HTMLTextAreaElement;
    expect(JSON.parse(editor.value)).toEqual({ tenant: "acme", note: "hi" });
    expect(screen.getByText(/Locked, can't be changed: tenant/)).toBeTruthy();
    // Replacing the whole body would wipe the locked keys, so there is no picker.
    expect(screen.queryByRole("button", { name: /insert a configuration variable/i })).toBeNull();

    fireEvent.change(editor, { target: { value: '{"tenant": "acme", "note": "bye"}' } });
    expect(JSON.parse(onChange.mock.lastCall![0])).toEqual({ note: "bye" });
    expect(screen.queryByText(/is locked/)).toBeNull();

    fireEvent.change(editor, { target: { value: '{"note": "bye"}' } });
    expect(screen.getByText(`"tenant" is locked and can't be changed or removed.`)).toBeTruthy();
  });
});

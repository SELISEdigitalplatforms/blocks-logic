import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { VariablesEditor } from "./variables-editor";

vi.mock("@/services/secret.service", () => ({
  secretService: {
    getAll: vi.fn().mockResolvedValue([
      { id: "s1", name: "stripe-api-key", tags: ["payments"] },
      { id: "s2", name: "sendgrid-api-key", tags: ["mail"] },
    ]),
  },
}));

describe("VariablesEditor", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("binds a row to a configuration variable by id, never by name", async () => {
    // The id is what FunctionEnvelopeBuilder resolves against, and it survives a rename.
    const onChange = vi.fn();
    renderWithProviders(
      <VariablesEditor value={[{ key: "STRIPE_API_KEY", value: "" }]} onChange={onChange} />,
    );

    // The trigger stays disabled until the catalog lands, and swaps DOM nodes when it enables
    // (tooltip wrapper -> popover trigger), so it has to be re-queried rather than held onto.
    const bind = () =>
      screen.getByRole("button", {
        name: /bind variable 1 value to a configuration variable/i,
      }) as HTMLButtonElement;
    await waitFor(() => expect(bind().disabled).toBe(false));
    await userEvent.click(bind());
    await userEvent.click(await screen.findByRole("option", { name: /stripe-api-key/ }));

    expect(onChange).toHaveBeenCalledWith([{ key: "STRIPE_API_KEY", value: "{{secret.s1}}" }]);
  });

  it("shows a bound row as the variable's name rather than its stored reference", async () => {
    renderWithProviders(
      <VariablesEditor
        value={[{ key: "STRIPE_API_KEY", value: "{{secret.s1}}" }]}
        onChange={vi.fn()}
      />,
    );

    expect(await screen.findByText("stripe-api-key")).toBeTruthy();
    expect(screen.queryByDisplayValue("{{secret.s1}}")).toBeNull();
    // The value field is replaced by the binding, so there is nothing to type into.
    expect(screen.queryByLabelText("Variable 1 value")).toBeNull();
  });

  it("unbinds back to an editable text value", async () => {
    const onChange = vi.fn();
    renderWithProviders(
      <VariablesEditor
        value={[{ key: "STRIPE_API_KEY", value: "{{secret.s1}}" }]}
        onChange={onChange}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: /unbind variable 1 value/i }));

    expect(onChange).toHaveBeenCalledWith([{ key: "STRIPE_API_KEY", value: "" }]);
  });

  it("flags a reference whose variable no longer exists", async () => {
    // The run would fail at invoke with an unresolved reference; saying so here is the only
    // place the author can act on it.
    renderWithProviders(
      <VariablesEditor
        value={[{ key: "STRIPE_API_KEY", value: "{{secret.gone}}" }]}
        onChange={vi.fn()}
      />,
    );

    expect(await screen.findByText(/no longer exists/i)).toBeTruthy();
  });

  it("does not warn about a credential-shaped key once it is bound", async () => {
    renderWithProviders(
      <VariablesEditor
        value={[{ key: "STRIPE_API_KEY", value: "{{secret.s1}}" }]}
        onChange={vi.fn()}
      />,
    );

    await screen.findByText("stripe-api-key");
    expect(screen.queryByText(/looks like a credential/i)).toBeNull();
  });

  it("warns when a credential-shaped key is given a typed value", () => {
    renderWithProviders(
      <VariablesEditor
        value={[{ key: "STRIPE_API_KEY", value: "sk_live_9" }]}
        onChange={vi.fn()}
      />,
    );

    expect(screen.getByText(/looks like a credential/i)).toBeTruthy();
  });

  it("keeps a value that only embeds a reference as editable text", async () => {
    // "Bearer {{secret.s1}}" is not a binding, and no chip could represent it honestly.
    renderWithProviders(
      <VariablesEditor
        value={[{ key: "AUTH", value: "Bearer {{secret.s1}}" }]}
        onChange={vi.fn()}
      />,
    );

    expect(screen.getByDisplayValue("Bearer {{secret.s1}}")).toBeTruthy();
  });

  it("still reports a duplicate key on a bound row", async () => {
    renderWithProviders(
      <VariablesEditor
        value={[
          { key: "A", value: "{{secret.s1}}" },
          { key: "A", value: "plain" },
        ]}
        onChange={vi.fn()}
      />,
    );

    expect(screen.getAllByText("That key is already used.")).toHaveLength(2);
  });
});

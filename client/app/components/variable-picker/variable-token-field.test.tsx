import { describe, it, expect, vi } from "vitest";
import { fireEvent, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { VariableTokenField } from "./variable-token-field";
import { secretIdRef, varNameRef } from "./variable-ref";

const CATALOG = [{ id: "sec_abc123", name: "stripe-api-key", tags: ["payments"] }];

describe("VariableTokenField", () => {
  it("shows an id-keyed reference by name, inside the surrounding text", () => {
    renderWithProviders(
      <VariableTokenField
        value="Bearer {{secret.sec_abc123}}"
        onChange={vi.fn()}
        codec={secretIdRef}
        ariaLabel="Header value"
        variables={CATALOG}
      />,
    );

    const field = screen.getByLabelText("Header value") as HTMLInputElement;
    expect(field.value).toBe("Bearer {{variable.stripe-api-key}}");
    expect(field.value).not.toContain("sec_abc123");
  });

  it("stores the id again when the display text is edited", async () => {
    const onChange = vi.fn();
    renderWithProviders(
      <VariableTokenField
        value="{{secret.sec_abc123}}"
        onChange={onChange}
        codec={secretIdRef}
        ariaLabel="Header value"
        variables={CATALOG}
      />,
    );

    await userEvent.type(screen.getByLabelText("Header value"), "!");

    expect(onChange).toHaveBeenLastCalledWith("{{secret.sec_abc123}}!");
  });

  it("leaves a name-keyed token exactly as the author typed it", () => {
    renderWithProviders(
      <VariableTokenField
        value="Bearer {{$VAR.stripe-api-key}}"
        onChange={vi.fn()}
        codec={varNameRef}
        ariaLabel="Header value"
        variables={CATALOG}
      />,
    );

    expect((screen.getByLabelText("Header value") as HTMLInputElement).value).toBe(
      "Bearer {{$VAR.stripe-api-key}}",
    );
  });

  it("locks rather than exposing a reference the catalog cannot name", () => {
    // Editing display text that still holds an unnameable key would either show the key or
    // silently drop it on the round trip. Neither is acceptable, so the field refuses to edit.
    const { container } = renderWithProviders(
      <VariableTokenField
        value="Bearer {{secret.sec_gone}}"
        onChange={vi.fn()}
        codec={secretIdRef}
        ariaLabel="Header value"
        variables={CATALOG}
      />,
    );

    expect(container.textContent).not.toContain("sec_gone");
    expect(screen.getByText(/no longer exists/i)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Clear" })).toBeTruthy();
  });

  it("clears a broken reference on request", async () => {
    const onChange = vi.fn();
    renderWithProviders(
      <VariableTokenField
        value="{{secret.sec_gone}}"
        onChange={onChange}
        codec={secretIdRef}
        ariaLabel="Header value"
        variables={CATALOG}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: "Clear" }));

    expect(onChange).toHaveBeenCalledWith("");
  });

  it("flags that a value uses a variable", () => {
    renderWithProviders(
      <VariableTokenField
        value="{{secret.sec_abc123}}"
        onChange={vi.fn()}
        codec={secretIdRef}
        ariaLabel="Header value"
        variables={CATALOG}
      />,
    );

    expect(screen.getByText("uses a variable")).toBeTruthy();
  });

  it("inserts at the caret rather than appending", async () => {
    const onChange = vi.fn();
    renderWithProviders(
      <VariableTokenField
        value="Bearer end"
        onChange={onChange}
        codec={varNameRef}
        ariaLabel="Header value"
        variables={CATALOG}
      />,
    );

    const field = screen.getByLabelText("Header value") as HTMLInputElement;
    // Click first: it lands the caret at the end, so the selection has to be set after it.
    await userEvent.click(field);
    field.setSelectionRange(7, 7);
    fireEvent.select(field);
    await userEvent.click(
      screen.getByRole("button", { name: /insert a configuration variable into header value/i }),
    );
    await userEvent.click(screen.getByRole("option", { name: /stripe-api-key/ }));

    expect(onChange).toHaveBeenCalledWith("Bearer {{$VAR.stripe-api-key}}end");
  });
});

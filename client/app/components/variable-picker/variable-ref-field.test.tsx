import { describe, it, expect, vi } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { VariableRefField } from "./variable-ref-field";
import { secretIdRef, varNameRef } from "./variable-ref";

const CATALOG = [{ id: "sec_abc123", name: "stripe-api-key", tags: ["payments"] }];

describe("VariableRefField", () => {
  it("shows the variable's name for a bound value", () => {
    renderWithProviders(
      <VariableRefField
        value="{{secret.sec_abc123}}"
        onChange={vi.fn()}
        codec={secretIdRef}
        ariaLabel="value"
        variables={CATALOG}
      />,
    );

    expect(screen.getByText("stripe-api-key")).toBeTruthy();
  });

  it("never renders the id, not even as a fallback when the catalog cannot name it", () => {
    // The deleted-variable path is exactly where a lazy `?? id` fallback would leak one.
    const { container } = renderWithProviders(
      <VariableRefField
        value="{{secret.sec_abc123}}"
        onChange={vi.fn()}
        codec={secretIdRef}
        ariaLabel="value"
        variables={[]}
      />,
    );

    expect(container.textContent).not.toContain("sec_abc123");
    expect(screen.getByText("Unavailable variable")).toBeTruthy();
    expect(screen.getByText(/no longer exists/i)).toBeTruthy();
  });

  it("does not render the reference token itself either", () => {
    const { container } = renderWithProviders(
      <VariableRefField
        value="{{secret.sec_abc123}}"
        onChange={vi.fn()}
        codec={secretIdRef}
        ariaLabel="value"
        variables={CATALOG}
      />,
    );

    expect(container.textContent).not.toContain("{{secret.");
    expect(container.querySelector("input")).toBeNull();
  });

  it("keeps a plain value editable", () => {
    renderWithProviders(
      <VariableRefField
        value="acct_1P9"
        onChange={vi.fn()}
        codec={secretIdRef}
        ariaLabel="value"
        variables={CATALOG}
      />,
    );

    expect(screen.getByDisplayValue("acct_1P9")).toBeTruthy();
  });

  it("leaves a name-keyed token visible, because a name is not a secret", () => {
    // Proxy's `{{$VAR.name}}` is the syntax its users type; hiding it would help nobody.
    renderWithProviders(
      <VariableRefField
        value="{{$VAR.stripe-api-key}}"
        onChange={vi.fn()}
        codec={varNameRef}
        ariaLabel="value"
        variables={CATALOG}
      />,
    );

    expect(screen.getByText("stripe-api-key")).toBeTruthy();
  });
});

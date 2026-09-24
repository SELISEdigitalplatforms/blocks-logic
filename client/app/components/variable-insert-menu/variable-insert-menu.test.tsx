import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { SecretListItem } from "@/models/secret";
import { VariableInsertMenu } from "./variable-insert-menu";

const VARIABLES: SecretListItem[] = [
  { id: "s-1", name: "stripe-api-key", tags: [] },
  { id: "s-2", name: "sendgrid-api-key", tags: [] },
];

const trigger = () => screen.getByRole("button", { name: "Insert into value" });

describe("VariableInsertMenu", () => {
  it("lists the variables and hands back the picked name", async () => {
    const user = userEvent.setup();
    const onPick = vi.fn();
    renderWithProviders(
      <VariableInsertMenu variables={VARIABLES} onPick={onPick} ariaLabel="Insert into value" />,
    );

    await user.click(trigger());
    expect(screen.getByRole("menuitem", { name: "sendgrid-api-key" })).toBeTruthy();
    await user.click(screen.getByRole("menuitem", { name: "stripe-api-key" }));

    expect(onPick).toHaveBeenCalledWith("stripe-api-key");
  });

  it.each([
    ["loading", { variablesLoading: true, variables: VARIABLES }],
    ["errored", { variablesError: true, variables: VARIABLES }],
    ["empty", { variables: [] }],
  ])("is a disabled trigger while %s", (_, props) => {
    renderWithProviders(
      <VariableInsertMenu {...props} onPick={vi.fn()} ariaLabel="Insert into value" />,
    );

    expect(trigger().hasAttribute("disabled")).toBe(true);
  });
});

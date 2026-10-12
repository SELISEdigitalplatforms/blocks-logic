import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SecretListItem } from "@/models/secret";
import { VariablePicker } from "./variable-picker";

const VARIABLES: SecretListItem[] = [
  { id: "s1", name: "stripe-api-key", tags: ["payments", "live"] },
  { id: "s2", name: "sendgrid-api-key", tags: ["mail"] },
  { id: "s3", name: "adyen-token", tags: ["payments"] },
];

const options = () => screen.getAllByRole("option").map((each) => each.textContent);

describe("VariablePicker", () => {
  it("lists every variable it is given", () => {
    render(<VariablePicker variables={VARIABLES} onPick={vi.fn()} />);

    expect(screen.getAllByRole("option")).toHaveLength(3);
  });

  it("filters by name as you type", async () => {
    render(<VariablePicker variables={VARIABLES} onPick={vi.fn()} />);

    await userEvent.type(screen.getByLabelText("Search variables"), "sendgrid");

    expect(options()).toHaveLength(1);
    expect(screen.getByRole("option", { name: /sendgrid-api-key/ })).toBeTruthy();
  });

  it("matches on tag text too, so a half-remembered tag still finds the key", async () => {
    render(<VariablePicker variables={VARIABLES} onPick={vi.fn()} />);

    await userEvent.type(screen.getByLabelText("Search variables"), "mail");

    expect(options()).toHaveLength(1);
    expect(screen.getByRole("option", { name: /sendgrid-api-key/ })).toBeTruthy();
  });

  it("offers each tag once, sorted", () => {
    render(<VariablePicker variables={VARIABLES} onPick={vi.fn()} />);

    const tags = screen.getByRole("group", { name: "Filter by tag" }).querySelectorAll("button");

    expect([...tags].map((each) => each.textContent)).toEqual(["live", "mail", "payments"]);
  });

  it("narrows to a tag when one is pressed", async () => {
    render(<VariablePicker variables={VARIABLES} onPick={vi.fn()} />);

    await userEvent.click(screen.getByRole("button", { name: "payments" }));

    expect(options()).toHaveLength(2);
    expect(screen.queryByRole("option", { name: /sendgrid-api-key/ })).toBeNull();
  });

  it("clears the tag when the pressed one is pressed again", async () => {
    render(<VariablePicker variables={VARIABLES} onPick={vi.fn()} />);
    const payments = screen.getByRole("button", { name: "payments" });

    await userEvent.click(payments);
    expect(payments.getAttribute("aria-pressed")).toBe("true");

    await userEvent.click(payments);

    expect(payments.getAttribute("aria-pressed")).toBe("false");
    expect(options()).toHaveLength(3);
  });

  it("combines the tag filter with the search term", async () => {
    render(<VariablePicker variables={VARIABLES} onPick={vi.fn()} />);

    await userEvent.click(screen.getByRole("button", { name: "payments" }));
    await userEvent.type(screen.getByLabelText("Search variables"), "adyen");

    expect(options()).toHaveLength(1);
    expect(screen.getByRole("option", { name: /adyen-token/ })).toBeTruthy();
  });

  it("says so when nothing matches rather than showing an empty box", async () => {
    render(<VariablePicker variables={VARIABLES} onPick={vi.fn()} />);

    await userEvent.type(screen.getByLabelText("Search variables"), "nothing-like-this");

    expect(screen.getByText("No variable matches that search.")).toBeTruthy();
  });

  it("distinguishes an empty catalog from a failed one", () => {
    const { rerender } = render(<VariablePicker variables={[]} onPick={vi.fn()} />);
    expect(screen.getByText(/add one in Secret management/i)).toBeTruthy();

    rerender(<VariablePicker variables={[]} isError onPick={vi.fn()} />);
    expect(screen.getByText("Unable to load variables.")).toBeTruthy();

    rerender(<VariablePicker variables={[]} isLoading onPick={vi.fn()} />);
    expect(screen.getByText("Loading variables…")).toBeTruthy();
  });

  it("hides the tag row when nothing is tagged", () => {
    render(<VariablePicker variables={[{ id: "s1", name: "plain", tags: [] }]} onPick={vi.fn()} />);

    expect(screen.queryByRole("group", { name: "Filter by tag" })).toBeNull();
  });

  it("shows every row again if the active tag disappears from the catalog", async () => {
    // A refetch can drop a tag out from under a held-open filter; an empty list with no
    // visible cause would look like the catalog itself had emptied.
    const { rerender } = render(<VariablePicker variables={VARIABLES} onPick={vi.fn()} />);

    await userEvent.click(screen.getByRole("button", { name: "payments" }));
    expect(options()).toHaveLength(2);

    rerender(<VariablePicker variables={[VARIABLES[1]]} onPick={vi.fn()} />);

    expect(options()).toHaveLength(1);
    expect(screen.getByRole("option", { name: /sendgrid-api-key/ })).toBeTruthy();
  });

  it("hands the whole row back so the caller can build its own token", async () => {
    const onPick = vi.fn();
    render(<VariablePicker variables={VARIABLES} onPick={onPick} />);

    await userEvent.click(screen.getByRole("option", { name: /stripe-api-key/ }));

    expect(onPick).toHaveBeenCalledWith(VARIABLES[0]);
  });

  it("ticks the variable already bound", () => {
    render(<VariablePicker variables={VARIABLES} selectedId="s2" onPick={vi.fn()} />);

    expect(
      screen.getByRole("option", { name: /sendgrid-api-key/ }).getAttribute("aria-selected"),
    ).toBe("true");
    expect(
      screen.getByRole("option", { name: /stripe-api-key/ }).getAttribute("aria-selected"),
    ).toBe("false");
  });
});

describe("VariablePicker — what it is allowed to render", () => {
  it("renders names and tags, and never an id", () => {
    const { container } = render(<VariablePicker variables={VARIABLES} onPick={vi.fn()} />);

    // The ids are the one thing in the row that must not reach the DOM.
    VARIABLES.forEach((variable) => {
      expect(container.textContent).not.toContain(variable.id);
      expect(container.innerHTML).not.toContain(`>${variable.id}<`);
    });
    expect(container.textContent).toContain("stripe-api-key");
  });
});

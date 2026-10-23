import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import type { IEventCatalogItem } from "../../types/event.types";
import { EventEditDialog } from "./event-edit-dialog";

// Monaco does not run in jsdom: a textarea with the same value/onChange contract stands in.
vi.mock("../code-editor", () => ({
  CodeEditor: ({ value, onChange }: { value: string; onChange?: (value: string) => void }) => (
    <textarea aria-label="payload" value={value} onChange={(event) => onChange?.(event.target.value)} />
  ),
}));

const ORDER_PAID = "order.paid";

const CATALOG: IEventCatalogItem[] = [
  { type: "SendMail", title: "Send mail", description: "Sends an email.", publicAllowed: false, payloadTemplate: { to: "" } },
  { type: "FunctionEvent", title: "Function event", description: "Starts another function.", publicAllowed: true, payloadTemplate: { name: ORDER_PAID } },
];

const renderDialog = (props: Partial<Parameters<typeof EventEditDialog>[0]> = {}) => {
  const onSubmit = vi.fn();
  render(
    <EventEditDialog
      open
      onOpenChange={vi.fn()}
      available={CATALOG}
      initial={null}
      isPublic={false}
      isSaving={false}
      serverErrors={[]}
      onSubmit={onSubmit}
      {...props}
    />,
  );
  return { onSubmit };
};

describe("EventEditDialog", () => {
  it("shows no editor and a disabled Save until a type is chosen", () => {
    renderDialog();
    expect(screen.queryByLabelText("payload")).toBeNull();
    expect((screen.getByRole("button", { name: "Save" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("edits an event: the type is fixed and the payload is pretty-printed", () => {
    renderDialog({ initial: { type: "FunctionEvent", payload: { name: ORDER_PAID } } });
    expect((screen.getByLabelText("payload") as HTMLTextAreaElement).value).toBe('{\n  "name": "order.paid"\n}');
    expect(screen.getByText(/must match/)).toBeTruthy();
  });

  it("saves the edited payload as an object", () => {
    const { onSubmit } = renderDialog({ initial: { type: "FunctionEvent", payload: { name: ORDER_PAID } } });

    fireEvent.change(screen.getByLabelText("payload"), { target: { value: '{"name":"order.done"}' } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(onSubmit).toHaveBeenCalledWith({ type: "FunctionEvent", payload: { name: "order.done" } });
  });

  it("invalid JSON shows the error and disables Save", () => {
    const { onSubmit } = renderDialog({ initial: { type: "SendMail", payload: { to: "a" } } });

    fireEvent.change(screen.getByLabelText("payload"), { target: { value: "{ nope" } });

    expect(screen.getByRole("alert").textContent).toContain("Not valid JSON");
    const save = screen.getByRole("button", { name: "Save" }) as HTMLButtonElement;
    expect(save.disabled).toBe(true);
    fireEvent.click(save);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("a FunctionEvent with a bad name cannot be saved", () => {
    renderDialog({ initial: { type: "FunctionEvent", payload: { name: "ok.name" } } });

    fireEvent.change(screen.getByLabelText("payload"), { target: { value: '{"name":"Bad"}' } });

    expect(screen.getByRole("alert").textContent).toContain("not valid");
    expect((screen.getByRole("button", { name: "Save" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("says why only some types are offered to a public function", () => {
    renderDialog({ isPublic: true });
    expect(screen.getByText(/public HTTP trigger/)).toBeTruthy();
  });

  it("shows the messages the server gave for this event", () => {
    renderDialog({
      initial: { type: "SendMail", payload: { to: "a" } },
      serverErrors: ['payload: unknown field "x"'],
    });
    expect(screen.getByText('payload: unknown field "x"')).toBeTruthy();
  });

  it("says when every type is already added", () => {
    renderDialog({ available: [] });
    expect(screen.getByText(/already added/)).toBeTruthy();
  });
});

import React from "react";
import { act, fireEvent, render, renderHook, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { useDebounce } from "@/hooks/use-debounce";
import { getApiPath, getApiUrl } from "@/lib/get-api-path";
import { AddNodeMenu } from "@/modules/workflow/components/workflow-editor/add-node-menu";
import { formatCellValue } from "@/modules/workflow/components/node-inspector/shared/input-panel/utils/format.util";
import { ExpressionHighlighter } from "@/modules/workflow/components/node-inspector/form-builder/utils/expression-highlighter";

describe("useDebounce", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("returns the debounced value after the delay", () => {
    const { result, rerender } = renderHook(
      ({ v }: { v: string }) => useDebounce(v, 200),
      { initialProps: { v: "a" } },
    );
    expect(result.current).toBe("a");
    rerender({ v: "b" });
    expect(result.current).toBe("a");
    act(() => vi.advanceTimersByTime(200));
    expect(result.current).toBe("b");
  });
});

describe("get-api-path", () => {
  it("returns the api path and builds a url", () => {
    expect(getApiPath("svc")).toBe("/api");
    expect(getApiUrl("svc", "endpoint")).toContain("/api/endpoint");
  });
});

describe("formatCellValue", () => {
  it("formats each value kind", () => {
    expect(formatCellValue(null)).toBe("null");
    expect(formatCellValue(undefined)).toBe("undefined");
    expect(formatCellValue("hi")).toBe('"hi"');
    expect(formatCellValue(5)).toBe("5");
    expect(formatCellValue(true)).toBe("true");
    expect(formatCellValue({ a: 1 })).toBe('{"a":1}');
  });
});

describe("AddNodeMenu", () => {
  it("renders templates and reports a selection", async () => {
    const onAddNode = vi.fn();
    const user = userEvent.setup();
    render(<AddNodeMenu onAddNode={onAddNode} />);
    await user.click(screen.getByText("Add Step"));
    await user.click(await screen.findByText("Trigger"));
    expect(onAddNode).toHaveBeenCalledWith(
      "trigger",
      "Trigger",
      "Start the workflow",
    );
  });
});

describe("ExpressionHighlighter", () => {
  it("returns the child unchanged when highlighting is disabled", () => {
    renderWithProviders(
      <ExpressionHighlighter value="text" disableHighlighting>
        <input data-testid="raw" />
      </ExpressionHighlighter>,
    );
    expect(screen.getByTestId("raw")).toBeTruthy();
  });

  it("highlights valid and invalid expressions and forwards scroll", () => {
    const onScroll = vi.fn();
    const { container } = renderWithProviders(
      <ExpressionHighlighter value={'start {{$json.input.name}} mid {{$unknown}} end'}>
        <textarea data-testid="ta" onScroll={onScroll} />
      </ExpressionHighlighter>,
      {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        seedWorkflow: (store: any) =>
          store.getState().addNode({
            id: "a",
            name: "webhook_main",
            type: "webhook",
            position: { x: 0, y: 0 },
            parameters: {},
          }),
      },
    );
    // highlighted expression tokens render as spans in the backdrop
    expect(container.textContent).toContain("{{$json.input.name}}");
    // scrolling the enhanced child forwards to the backdrop and the child handler
    fireEvent.scroll(screen.getByTestId("ta"));
    expect(onScroll).toHaveBeenCalled();
  });

  it("highlights node-scoped expressions against the workflow nodes", () => {
    const { container } = renderWithProviders(
      <ExpressionHighlighter
        value={'{{$node["Task"].json.output.value}}'}
        isMultiline={false}
      >
        <input data-testid="ni" />
      </ExpressionHighlighter>,
      {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        seedWorkflow: (store: any) =>
          store.getState().addNode({
            id: "t",
            name: "Task",
            type: "action",
            position: { x: 0, y: 0 },
            parameters: {},
          }),
      },
    );
    expect(container.textContent).toContain('{{$node["Task"].json.output.value}}');
  });
});

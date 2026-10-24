import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { makeHookWrapper } from "@/test-utils/test-providers/render";
import { IFunctionDetail } from "../types/function.types";

vi.mock("../services/function.service", () => ({ functionService: { saveFunction: vi.fn() } }));

import { useFunctionEditor } from "./use-function-editor";
import { useFunctionEditorStore } from "../store/function-editor-store";

const detail = (patch: Partial<IFunctionDetail> = {}) =>
  ({
    id: "fn_1",
    indexJs: "server v1",
    packageJson: "{}",
    variables: [],
    trigger: {},
    lastUpdatedDate: "2026-10-23T10:00:00Z",
    ...patch,
  }) as unknown as IFunctionDetail;

const setup = (initial: IFunctionDetail) =>
  renderHook(({ fn }) => useFunctionEditor(fn.id, fn), {
    initialProps: { fn: initial },
    wrapper: makeHookWrapper(),
  });

describe("useFunctionEditor — server changes reach the editor", () => {
  beforeEach(() => {
    act(() => useFunctionEditorStore.getState().hydrate({ indexJs: "", packageJson: "{}", variables: [] } as never));
  });

  it("shows a newer server copy when there are no unsaved edits", () => {
    const hook = setup(detail());
    expect(useFunctionEditorStore.getState().indexJs).toBe("server v1");

    hook.rerender({ fn: detail({ indexJs: "server v2", lastUpdatedDate: "2026-10-23T11:00:00Z" }) });

    expect(useFunctionEditorStore.getState().indexJs).toBe("server v2");
  });

  it("never overwrites unsaved edits", () => {
    const hook = setup(detail());
    act(() => useFunctionEditorStore.getState().setIndexJs("my edit"));

    hook.rerender({ fn: detail({ indexJs: "server v2", lastUpdatedDate: "2026-10-23T11:00:00Z" }) });

    expect(useFunctionEditorStore.getState().indexJs).toBe("my edit");
  });

  it("loads another function even with unsaved edits", () => {
    const hook = setup(detail());
    act(() => useFunctionEditorStore.getState().setIndexJs("my edit"));

    hook.rerender({ fn: detail({ id: "fn_2", indexJs: "other function" }) });

    expect(useFunctionEditorStore.getState().indexJs).toBe("other function");
  });
});

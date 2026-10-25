import { describe, expect, it } from "vitest";
import type { WorkflowNode } from "../models/node.model";
import { toPersistedNode } from "./to-persisted-node";

describe("toPersistedNode", () => {
  it("H9: sends only the fields the server stores", () => {
    const handle = { sources: [{ key: "source", label: "" }], targets: [{ key: "target", label: "" }] };
    const editorNode = {
      id: "n1",
      name: "Code",
      category: "transform",
      type: "code",
      version: "v2",
      position: { x: 1, y: 2 },
      handle,
      parameters: { code: "return items;" },
      settings: { retry: 1 },
      pinData: null,
      // editor-only fields
      data: { hasHandleArrow: true },
      selected: true,
      measured: { width: 100, height: 60 },
      icon: "icon",
      title: "Code",
      handleSpec: { source: ["source"], target: ["target"] },
      description: "d",
    } as unknown as WorkflowNode;

    const persisted = toPersistedNode(editorNode);

    expect(Object.keys(persisted).sort()).toEqual(
      ["category", "handle", "id", "name", "parameters", "pinData", "position", "settings", "type", "version"],
    );
    expect(persisted.handle).toBe(handle);
    expect(persisted.parameters).toEqual({ code: "return items;" });
  });

  it("keeps a missing handle missing, as an old node had it", () => {
    const persisted = toPersistedNode({ id: "n1", position: { x: 0, y: 0 } } as unknown as WorkflowNode);
    expect(JSON.parse(JSON.stringify(persisted))).toEqual({ id: "n1", position: { x: 0, y: 0 } });
  });
});

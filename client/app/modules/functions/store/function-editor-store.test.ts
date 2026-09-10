import { beforeEach, describe, expect, it } from "vitest";
import { useFunctionEditorStore } from "./function-editor-store";

const snapshot = {
  indexJs: "export default async function handler() {}",
  packageJson: "{}",
  limits: {
    cpuMillicores: 100,
    memoryMb: 192,
    timeoutSeconds: 10,
    concurrency: 2,
    requestsPerMinute: null,
    requestsPerDay: null,
  },
  retry: { attempts: 1, backoff: "None" as const, initialDelaySeconds: 5, maxDelaySeconds: 300 },
  trigger: {
    httpEnabled: true,
    authMode: "Token" as const,
    roles: [],
    permissions: [],
    roleMatch: "Any" as const,
    permissionMatch: "Any" as const,
    workflowEnabled: true,
  },
  outputActions: [],
  variables: [],
};

beforeEach(() => {
  useFunctionEditorStore.getState().reset();
});

describe("useFunctionEditorStore", () => {
  it("is not dirty right after hydrating", () => {
    useFunctionEditorStore.getState().hydrate(snapshot);
    expect(useFunctionEditorStore.getState().isDirty).toBe(false);
  });

  it("becomes dirty when a field changes after hydration", () => {
    useFunctionEditorStore.getState().hydrate(snapshot);
    useFunctionEditorStore.getState().setIndexJs("export default async () => 42;");
    expect(useFunctionEditorStore.getState().isDirty).toBe(true);
  });

  it("is not dirty again once the value returns to the saved snapshot", () => {
    useFunctionEditorStore.getState().hydrate(snapshot);
    useFunctionEditorStore.getState().setIndexJs("changed");
    useFunctionEditorStore.getState().setIndexJs(snapshot.indexJs);
    expect(useFunctionEditorStore.getState().isDirty).toBe(false);
  });

  it("re-hydrating (as a save response does) clears the dirty flag against the new snapshot", () => {
    useFunctionEditorStore.getState().hydrate(snapshot);
    useFunctionEditorStore.getState().setIndexJs("changed");
    expect(useFunctionEditorStore.getState().isDirty).toBe(true);

    useFunctionEditorStore.getState().hydrate({ ...snapshot, indexJs: "changed" });
    expect(useFunctionEditorStore.getState().isDirty).toBe(false);
  });

  it("changing limits, trigger, output actions or variables all mark the editor dirty", () => {
    useFunctionEditorStore.getState().hydrate(snapshot);

    useFunctionEditorStore.getState().setLimits({ ...snapshot.limits, memoryMb: 256 });
    expect(useFunctionEditorStore.getState().isDirty).toBe(true);

    useFunctionEditorStore.getState().hydrate(snapshot);
    useFunctionEditorStore.getState().setTrigger({ ...snapshot.trigger, httpEnabled: false });
    expect(useFunctionEditorStore.getState().isDirty).toBe(true);

    useFunctionEditorStore.getState().hydrate(snapshot);
    useFunctionEditorStore
      .getState()
      .setOutputActions([{ id: "a1", kind: "ExternalHttp", enabled: true, url: "", method: "POST", headers: {}, bodyTemplate: null, timeoutSeconds: 30 }]);
    expect(useFunctionEditorStore.getState().isDirty).toBe(true);

    useFunctionEditorStore.getState().hydrate(snapshot);
    useFunctionEditorStore.getState().setVariables([{ key: "K", value: "V" }]);
    expect(useFunctionEditorStore.getState().isDirty).toBe(true);
  });

  it("reset clears the editor back to an empty, non-dirty state", () => {
    useFunctionEditorStore.getState().hydrate(snapshot);
    useFunctionEditorStore.getState().setIndexJs("changed");
    useFunctionEditorStore.getState().reset();

    const state = useFunctionEditorStore.getState();
    expect(state.indexJs).toBe("");
    expect(state.isDirty).toBe(false);
    expect(state.savedSnapshot).toBeNull();
  });
});

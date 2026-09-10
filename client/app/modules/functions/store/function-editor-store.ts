import { create } from "zustand";
import {
  IFunctionLimits,
  IOutputAction,
  IRetryPolicy,
  ITriggerConfig,
  IVariableBinding,
} from "../types/function.types";

export type EditorFile = "index.js" | "package.json";

export interface FunctionEditorSnapshot {
  indexJs: string;
  packageJson: string;
  limits: IFunctionLimits;
  retry: IRetryPolicy;
  trigger: ITriggerConfig;
  outputActions: IOutputAction[];
  variables: IVariableBinding[];
}

interface FunctionEditorState extends FunctionEditorSnapshot {
  activeFile: EditorFile;
  testInput: string;
  savedSnapshot: FunctionEditorSnapshot | null;
  isDirty: boolean;

  setActiveFile: (file: EditorFile) => void;
  setIndexJs: (value: string) => void;
  setPackageJson: (value: string) => void;
  setLimits: (value: IFunctionLimits) => void;
  setRetry: (value: IRetryPolicy) => void;
  setTrigger: (value: ITriggerConfig) => void;
  setOutputActions: (value: IOutputAction[]) => void;
  setVariables: (value: IVariableBinding[]) => void;
  setTestInput: (value: string) => void;
  /** Loads a function's config into the editor and clears the dirty flag — call on fetch/save. */
  hydrate: (snapshot: FunctionEditorSnapshot) => void;
  reset: () => void;
}

const emptySnapshot: FunctionEditorSnapshot = {
  indexJs: "",
  packageJson: "",
  limits: {
    cpuMillicores: 100,
    memoryMb: 192,
    timeoutSeconds: 10,
    concurrency: 2,
    requestsPerMinute: null,
    requestsPerDay: null,
  },
  retry: { attempts: 1, backoff: "None", initialDelaySeconds: 5, maxDelaySeconds: 300 },
  trigger: {
    httpEnabled: true,
    authMode: "Token",
    roles: [],
    permissions: [],
    roleMatch: "Any",
    permissionMatch: "Any",
    workflowEnabled: true,
  },
  outputActions: [],
  variables: [],
};

const snapshotsEqual = (a: FunctionEditorSnapshot, b: FunctionEditorSnapshot) =>
  JSON.stringify(a) === JSON.stringify(b);

const currentSnapshot = (state: FunctionEditorState): FunctionEditorSnapshot => ({
  indexJs: state.indexJs,
  packageJson: state.packageJson,
  limits: state.limits,
  retry: state.retry,
  trigger: state.trigger,
  outputActions: state.outputActions,
  variables: state.variables,
});

export const useFunctionEditorStore = create<FunctionEditorState>((set) => ({
  ...emptySnapshot,
  activeFile: "index.js",
  testInput: "{}",
  savedSnapshot: null,
  isDirty: false,

  setActiveFile: (activeFile) => set({ activeFile }),

  setIndexJs: (indexJs) =>
    set((state) => {
      const next = { ...state, indexJs };
      return { indexJs, isDirty: !snapshotsEqual(currentSnapshot(next), state.savedSnapshot ?? emptySnapshot) };
    }),
  setPackageJson: (packageJson) =>
    set((state) => {
      const next = { ...state, packageJson };
      return {
        packageJson,
        isDirty: !snapshotsEqual(currentSnapshot(next), state.savedSnapshot ?? emptySnapshot),
      };
    }),
  setLimits: (limits) =>
    set((state) => {
      const next = { ...state, limits };
      return { limits, isDirty: !snapshotsEqual(currentSnapshot(next), state.savedSnapshot ?? emptySnapshot) };
    }),
  setRetry: (retry) =>
    set((state) => {
      const next = { ...state, retry };
      return { retry, isDirty: !snapshotsEqual(currentSnapshot(next), state.savedSnapshot ?? emptySnapshot) };
    }),
  setTrigger: (trigger) =>
    set((state) => {
      const next = { ...state, trigger };
      return { trigger, isDirty: !snapshotsEqual(currentSnapshot(next), state.savedSnapshot ?? emptySnapshot) };
    }),
  setOutputActions: (outputActions) =>
    set((state) => {
      const next = { ...state, outputActions };
      return {
        outputActions,
        isDirty: !snapshotsEqual(currentSnapshot(next), state.savedSnapshot ?? emptySnapshot),
      };
    }),
  setVariables: (variables) =>
    set((state) => {
      const next = { ...state, variables };
      return {
        variables,
        isDirty: !snapshotsEqual(currentSnapshot(next), state.savedSnapshot ?? emptySnapshot),
      };
    }),
  setTestInput: (testInput) => set({ testInput }),

  hydrate: (snapshot) => set({ ...snapshot, savedSnapshot: snapshot, isDirty: false }),

  reset: () => set({ ...emptySnapshot, savedSnapshot: null, isDirty: false, testInput: "{}" }),
}));

export const getFunctionEditorSnapshot = (): FunctionEditorSnapshot =>
  currentSnapshot(useFunctionEditorStore.getState());

import { createContext, useContext, useRef } from "react";
import { useStore } from "zustand";
import { createWorkflowStore, WorkflowStore, WorkflowState } from "./workflow-store";

const WorkflowStoreContext = createContext<WorkflowStore | null>(null);

export function WorkflowStoreProvider({
  children,
}: {
  children: React.ReactNode;
}) {
  const storeRef = useRef<WorkflowStore | null>(null);
  if (storeRef.current === null) {
    storeRef.current = createWorkflowStore();
  }
  return (
    <WorkflowStoreContext.Provider value={storeRef.current}>
      {children}
    </WorkflowStoreContext.Provider>
  );
}

// define 2 return types - selector and WorkflowState
export function useWorkflowStore<T>(selector: (state: WorkflowState) => T): T;
export function useWorkflowStore(): WorkflowState;
export function useWorkflowStore<T>(
  selector?: (state: WorkflowState) => T,
): T | WorkflowState {
  const store = useContext(WorkflowStoreContext);
  if (!store) {
    throw new Error(
      "useWorkflowStore must be used within a WorkflowStoreProvider",
    );
  }
  return useStore(store, selector as (state: WorkflowState) => T);
}

// returns the raw Zustand store object, basically to get the latest store, this is what provides the .getState()
export function useWorkflowStoreApi(): WorkflowStore {
  const store = useContext(WorkflowStoreContext);
  if (!store) {
    throw new Error(
      "useWorkflowStoreApi must be used within a WorkflowStoreProvider",
    );
  }
  return store;
}

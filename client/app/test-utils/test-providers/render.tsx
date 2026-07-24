import React from "react";
import { render, type RenderOptions } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ReactFlowProvider } from "@xyflow/react";
import {
  WorkflowStoreProvider,
  useWorkflowStoreApi,
  type WorkflowStore,
} from "@/modules/workflow/store";

type Options = RenderOptions & {
  /** Seeds the workflow store before the tree under test renders. */
  seedWorkflow?: (store: WorkflowStore) => void;
};

/** Captures the workflow store api and runs the seed callback exactly once. */
const Seeder: React.FC<{
  seed?: (store: WorkflowStore) => void;
  children: React.ReactNode;
}> = ({ seed, children }) => {
  const store = useWorkflowStoreApi();
  const done = React.useRef(false);
  if (!done.current) {
    done.current = true;
    seed?.(store);
  }
  return <>{children}</>;
};

/**
 * Renders a component wrapped in the providers most workflow UI relies on:
 * React Query, React Flow and the workflow zustand store.
 */
export const renderWithProviders = (ui: React.ReactElement, options: Options = {}) => {
  const { seedWorkflow, ...rest } = options;
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0 },
      mutations: { retry: false },
    },
  });

  const Wrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
    <QueryClientProvider client={queryClient}>
      <ReactFlowProvider>
        <WorkflowStoreProvider>
          <Seeder seed={seedWorkflow}>{children}</Seeder>
        </WorkflowStoreProvider>
      </ReactFlowProvider>
    </QueryClientProvider>
  );

  return render(ui, { wrapper: Wrapper, ...rest });
};

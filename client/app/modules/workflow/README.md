# Workflow module

The client module that provides the **visual workflow builder** for Blocks Logic (historically imported from a separate repository named `blocks-workflow-next-sub`; it now builds with this Vite app). It enables teams to design, configure, and execute automation workflows through a drag-and-drop canvas powered by React Flow, with support for triggers, actions, logic nodes, and real-time execution tracking.

---

## Table of Contents

- [Overview](#overview)
- [Directory Structure](#directory-structure)
- [Features](#features)
- [Key Concepts](#key-concepts)
- [Services Reference](#services-reference)
- [Hooks Reference](#hooks-reference)
- [Store Reference](#store-reference)
- [Models](#models)
- [Node Types](#node-types)
- [Utility Functions](#utility-functions)
- [Environment Variables](#environment-variables)
- [Running Tests](#running-tests)
- [Contributing](#contributing)
- [License](#license)

---

## Overview

| Domain              | Description                                                                              |
| ------------------- | ---------------------------------------------------------------------------------------- |
| Workflow CRUD       | Create, retrieve, update, delete, and duplicate workflows                                |
| Visual Editor       | Drag-and-drop canvas (React Flow) with node library panel, inspector, custom edges/nodes |
| Node System         | Typed, versioned nodes with JSON-schema-driven form builder for per-node configuration   |
| Auto-save           | Debounced dirty-flag driven auto-save with configurable interval                         |
| Execution Tracking  | List and inspect individual workflow executions with polling                             |
| Filter & Navigation | URL-synced filter query params for workflow list pagination                              |

---

## Directory Structure

```
workflow/
├── components/
│   ├── add-workflow/                  # Create-workflow dialog
│   ├── delete-workflow/               # Delete-confirmation dialog
│   ├── duplicate-workflow/            # Duplicate dialog with name schema
│   ├── toggle-status-workflow/        # Activate/deactivate toggle
│   ├── workflow-editor/               # Main React Flow canvas + add-node menu
│   ├── workflow-editor-controls/      # Zoom / fit-view / undo toolbar
│   ├── workflow-editor-edges/         # Custom edge components and type map
│   ├── workflow-editor-nodes/         # Custom node components (base, trigger, if, simple)
│   ├── workflow-execution/            # Execution detail view
│   ├── workflow-execution-list/       # Execution history list
│   ├── workflow-filter-toolbar/       # Search and filter bar
│   ├── workflow-list/                 # Paginated workflow table
│   ├── node-inspector/                # Right-panel node configuration
│   │   ├── form-builder/              # Dynamic form renderer (text, select, array, code, …)
│   │   ├── layouts/                   # Inspector layout variants (IO, listener, test)
│   │   └── shared/                    # Input/output/listen/test panels
│   ├── node-library-panel/            # Left-panel draggable node catalogue
│   └── node-schemas/                  # Per-node-type JSON form schemas
├── constant/
│   └── endpoint.constant.ts           # WORKFLOW_ENDPOINTS (8 routes)
├── hooks/
│   ├── use-workflow-api.ts            # TanStack Query hooks (CRUD + executions)
│   ├── use-workflow.ts                # Facade over Zustand store + React Flow instance
│   ├── use-auto-save-workflow.ts      # Debounced auto-save driven by isDirty
│   └── use-workflow-filter-query-params.ts  # URL search-param sync
├── models/
│   ├── workflow.model.ts              # Workflow, WorkflowSummary, WorkflowEdge, WorkflowStep
│   └── node.model.ts                 # WorkflowNode, EditorNode, WorkflowNodeDefinition
├── pages/
│   ├── workflows/                     # Workflow list page
│   └── workflow-details/              # Workflow editor page
├── services/
│   └── workflow.service.ts            # WorkflowService + workflowService singleton
├── store/
│   └── workflow-store.ts              # Zustand canvas state store (useWorkflowStore)
├── types/
│   └── workflow.service.type.ts       # Request/response payload interfaces
└── utils/
    ├── config-serializer.ts           # Serialize/deserialize WorkflowNode ↔ JSON
    ├── expression-parser.ts           # Template expression parser ({{node.field}})
    ├── add-copy-suffix.util.ts        # Auto-name duplicated workflows ("Copy of …")
    ├── find-free-position.ts          # Find non-overlapping canvas position for new nodes
    ├── workflow-execution-editor.util.ts  # Execution editor display helpers
    └── workflow-execution-list.util.ts    # Execution list formatting helpers
```

---

## Features

- **Visual Workflow Editor**: React Flow canvas with custom node/edge renderers, zoom controls, and an add-node context menu
- **Node Library Panel**: Searchable catalogue of all available trigger, action, and logic nodes
- **Dynamic Node Inspector**: Schema-driven form builder that renders the correct field type (text, select, checkbox, code editor, array, key-value, expression input) for each node's parameters
- **Typed Node System**: Nodes are versioned (`v1`/`v2`/`v3`) and categorised (`trigger` / `action` / `logic` / `transform`)
- **Auto-save**: `useAutoSaveWorkflow` debounces saves (default 10 s) and only fires when the canvas `isDirty` flag is set
- **Execution History**: `useGetWorkflowExecutions` and `useGetWorkflowExecutionById` poll every 5 seconds to provide live execution state
- **URL-synced Filters**: `useWorkflowFilterQueryParams` persists list filters in the browser URL

---

## Key Concepts

### Service → Hook → Component

All API calls go through `WorkflowService`. Hooks wrap service methods with TanStack Query. Components consume hooks; never call `http` or the service directly from a component.

### Zustand Canvas Store

`useWorkflowStore` (Zustand) owns the entire editor state: nodes (`nodesMap`), edges (`edgesMap`), `selectedNode`, `isDirty`, `isActive`, `isConfigModalOpen`, and `isPanelOpen`. The `useWorkflow` hook provides a convenient facade that combines store selectors with React Flow instance methods.

### Node Schemas

Each node type has a JSON schema in `components/node-schemas/`. The schema describes the parameters the node accepts and drives the `form-builder/` component to render the correct field types without hand-coding per-node forms.

### Config Serialisation

`serializeNodes` / `deserializeNodes` in `utils/config-serializer.ts` convert `WorkflowNode[]` to/from a plain JSON representation safe for API transport. Serialisation is applied automatically inside `WorkflowService.createWorkflow`, `updateWorkflow`, and `getWorkflowById`.

### Execution Polling

Both execution query hooks pass `refetchInterval: 5000`, providing near-real-time UI updates without a WebSocket connection.

---

## Services Reference

All methods live on `WorkflowService` and are accessed via the `workflowService` singleton exported from `services/workflow.service.ts`.

| Method                              | HTTP   | Endpoint                  | Description                                |
| ----------------------------------- | ------ | ------------------------- | ------------------------------------------ |
| `getWorkflows(payload)`             | POST   | `/Workflow/GetAll`        | Paginated workflow list                    |
| `getWorkflowById(payload)`          | GET    | `/Workflow/Get`           | Single workflow with deserialized nodes    |
| `createWorkflow(payload)`           | POST   | `/Workflow/Create`        | Create workflow, nodes auto-serialized     |
| `duplicateWorkflow(payload)`        | POST   | `/Workflow/Duplicate`     | Duplicate an existing workflow             |
| `updateWorkflow(payload)`           | PUT    | `/Workflow/Update`        | Save canvas changes, nodes auto-serialized |
| `deleteWorkflow(payload)`           | DELETE | `/Workflow/Delete`        | Delete workflow by ID                      |
| `getWorkflowExecutions(payload)`    | GET    | `/Workflow/GetExecutions` | Execution history for a workflow           |
| `getWorkflowExecutionById(payload)` | GET    | `/Workflow/GetExecution`  | Single execution detail                    |

---

## Hooks Reference

### API Hooks (`use-workflow-api.ts`)

| Hook                                   | Type     | Query Key                          | Description                                                 |
| -------------------------------------- | -------- | ---------------------------------- | ----------------------------------------------------------- |
| `useGetWorkflows(options)`             | Query    | `["workflows", options]`           | Paginated workflow list                                     |
| `useGetWorkflowById(payload)`          | Query    | `["workflow", payload]`            | Single workflow; guard on `id` or `projectKey`              |
| `useGetWorkflowExecutions(payload)`    | Query    | `["workflow-executions", payload]` | Execution list; polls every 5 s                             |
| `useGetWorkflowExecutionById(payload)` | Query    | `["workflow-execution", payload]`  | Single execution; polls every 5 s                           |
| `useCreateWorkflow()`                  | Mutation |;                                  | Creates workflow; invalidates `["workflows"]`               |
| `useDuplicateWorkflow()`               | Mutation |;                                  | Duplicates workflow; invalidates `["workflows"]`            |
| `useUpdateWorkflow()`                  | Mutation |;                                  | Saves changes; invalidates `["workflows"]` + `["workflow"]` |
| `useDeleteWorkflow()`                  | Mutation |;                                  | Deletes workflow; invalidates `["workflows"]`               |

### Canvas & UI Hooks

| Hook                             | Description                                                                                                                              |
| -------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| `useWorkflow()`                  | Facade combining `useWorkflowStore` selectors with React Flow instance methods; preferred way for components to interact with the editor |
| `useAutoSaveWorkflow(options)`   | Debounced auto-save (default 10 s) triggered by store `isDirty` flag; accepts `onSaveSuccess` / `onSaveError` callbacks                  |
| `useWorkflowFilterQueryParams()` | Syncs workflow list filter state (search, pagination) with URL query params                                                              |

---

## Store Reference

`useWorkflowStore` is a Zustand store defined in `store/workflow-store.ts`.

| State               | Type                         | Description                            |
| ------------------- | ---------------------------- | -------------------------------------- |
| `nodesMap`          | `Record<string, EditorNode>` | All canvas nodes keyed by ID           |
| `edgesMap`          | `Record<string, Edge>`       | All canvas edges keyed by ID           |
| `selectedNode`      | `EditorNode \| null`         | Currently selected node                |
| `isDirty`           | `boolean`                    | Unsaved changes flag; drives auto-save |
| `isActive`          | `boolean`                    | Workflow active/inactive status        |
| `isConfigModalOpen` | `boolean`                    | Node inspector modal visibility        |
| `isPanelOpen`       | `boolean`                    | Node library panel visibility          |
| `workflowId`        | `string \| null`             | Active workflow ID                     |
| `workflowName`      | `string`                     | Active workflow display name           |

Key store actions: `addNode`, `updateNode`, `deleteNode`, `duplicateNode`, `selectNode`, `deselectNode`, `createEdge`, `deleteEdge`, `setWorkflow`, `setWorkflowActive`, `resetWorkflow`.

---

## Models

### `workflow.model.ts`

| Type              | Description                                                                 |
| ----------------- | --------------------------------------------------------------------------- |
| `WorkflowSummary` | List-row shape (id, name, isActive, createdBy, dates, tags)                 |
| `WorkflowEdge`    | React Flow `Edge` extended with typed `sourceHandle`/`targetHandle`         |
| `Workflow`        | Full workflow (extends `WorkflowSummary`) with `nodes`, `edges`, `settings` |
| `WorkflowStep`    | Execution step record (id, name, type, order, configuration)                |

### `node.model.ts`

| Type                     | Description                                                                                                                               |
| ------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `NodeType`               | Union: `"if" \| "setfield" \| "webhookResponse" \| "manual" \| "event" \| "webhook" \| "agent" \| "sendMail" \| "httpRequest" \| "email"` |
| `NodeCategory`           | `"trigger" \| "action" \| "logic" \| "transform"`                                                                                         |
| `NodeVersion`            | `"v1" \| "v2" \| "v3"`                                                                                                                    |
| `WorkflowNode`           | API/storage node shape (extends React Flow `Node`)                                                                                        |
| `EditorNode`             | Canvas node shape (extends both `Node` and `WorkflowNode`)                                                                                |
| `WorkflowNodeDefinition` | Catalogue entry used by the node library panel                                                                                            |

---

## Node Types

| Category    | Node Types                                                        |
| ----------- | ----------------------------------------------------------------- |
| **Trigger** | `webhook`, `email`, `event`, `manual`                             |
| **Action**  | `httpRequest`, `sendMail`, `agent`, `setfield`, `webhookResponse` |
| **Logic**   | `if`                                                              |

Each has a corresponding schema in `components/node-schemas/` (e.g., `node-schema-action-httpRequest-v1.ts`) that feeds the dynamic form builder.

---

## Utility Functions

| File                                | Function(s)                          | Description                                                                        |
| ----------------------------------- | ------------------------------------ | ---------------------------------------------------------------------------------- |
| `config-serializer.ts`              | `serializeNodes`, `deserializeNodes` | Convert `WorkflowNode[]` ↔ API-safe JSON; called automatically by service methods |
| `expression-parser.ts`              | `parseExpression`                    | Parse `{{node.output.field}}` expression templates used in node parameters         |
| `add-copy-suffix.util.ts`           | `addCopySuffix`                      | Append "Copy of" / increment suffix for duplicated workflow names                  |
| `find-free-position.ts`             | `findFreePosition`                   | Calculate a non-overlapping `{x, y}` canvas coordinate for newly dropped nodes     |
| `workflow-execution-editor.util.ts` | various                              | Display helpers for the execution editor (status colors, step state mapping)       |
| `workflow-execution-list.util.ts`   | various                              | Display helpers for the execution list (date formatting, status labels)            |

---

## Environment Variables

| Variable                   | Description                                                                                                 |
| -------------------------- | ----------------------------------------------------------------------------------------------------------- |
| `NEXT_PUBLIC_API_BASE_URL` | Root API base URL; `API_BASES.UTILITIES` is derived from this constant and used by all `WORKFLOW_ENDPOINTS` |

---

## Running Tests

```bash
# Run all workflow module tests
npx vitest run workflow/

# Watch mode
npx vitest workflow/

# Coverage
npx vitest run --coverage workflow/
```

Tests use **Vitest** + **React Testing Library** + **MSW** for service and hook tests, and plain **Vitest** for utility unit tests.

---

## Contributing

See [CONTRIBUTING.md](./CONTRIBUTING.md) for coding guidelines, branching strategy, and the pull request process.

---

## License

[MIT](./LICENSE) © 2025 SELISE \<Blocks/>

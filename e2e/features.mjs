/**
 * Workflow E2E feature list — edit `enabled` and order here.
 * Run: npm run test:features
 *
 * Env: E2E_FEATURES=create,delete  or  E2E_FEATURES=all
 */

/** @type {{ id: string, name: string, enabled: boolean, spec: string }[]} */
export const WORKFLOW_FEATURES = [
  {
    id: "list",
    name: "Workflow – list",
    enabled: true,
    spec: "tests/workflow/workflow-list.spec.ts",
  },
  {
    id: "create",
    name: "Workflow – create",
    enabled: true,
    spec: "tests/workflow/create-workflow.spec.ts",
  },
  {
    id: "rename",
    name: "Workflow – rename",
    enabled: true,
    spec: "tests/workflow/rename-workflow.spec.ts",
  },
  {
    id: "duplicate",
    name: "Workflow – duplicate",
    enabled: true,
    spec: "tests/workflow/duplicate-workflow.spec.ts",
  },
  {
    id: "delete",
    name: "Workflow – delete",
    enabled: true,
    spec: "tests/workflow/delete-workflow.spec.ts",
  },
  {
    id: "editor",
    name: "Workflow – editor",
    enabled: true,
    spec: "tests/workflow/workflow-editor.spec.ts",
  },
]

export function resolveEnabledFeatures() {
  const override = process.env.E2E_FEATURES?.trim()

  if (!override || override === "all") {
    return WORKFLOW_FEATURES.filter((feature) => feature.enabled)
  }

  const ids = override.split(",").map((id) => id.trim()).filter(Boolean)
  /** @type {typeof WORKFLOW_FEATURES} */
  const selected = []

  for (const id of ids) {
    const feature = WORKFLOW_FEATURES.find((entry) => entry.id === id)
    if (!feature) {
      throw new Error(
        `Unknown E2E feature "${id}". Valid ids: ${WORKFLOW_FEATURES.map((f) => f.id).join(", ")}`,
      )
    }
    selected.push(feature)
  }

  return selected
}

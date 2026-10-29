import { expect, type Page } from "@playwright/test"
import { ConfirmationModalComponent } from "../po/components/confirmation-modal.component"
import { WorkflowEditorPage } from "../po/pages/workflow-editor.page"
import type { WorkflowImportFile } from "./filter-fixtures"
import { openWorkflowList } from "./workflow-helpers"

const EDITOR_URL = /\/workflow\/[^/?#]+(?:[?#].*)?$/

export type ImportedWorkflow = { name: string; url: string; workflowId: string }

function slug(name: string): string {
  return (
    name
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "") || "workflow"
  )
}

/**
 * Import a workflow file through the workflow list's Import control and land in its editor.
 *
 * import-workflow.tsx renders a hidden `<input type="file" accept="application/json,.json">`
 * behind the "Import" button (toolbar and empty state alike). use-import-workflow.ts uploads the
 * file, enqueues the import and navigates to `workflow/<id>` once the import notification arrives,
 * or when its backup poll (every 3 s for 60 s) finds a new workflow with the file's `name`.
 * So `file.name` must be unique.
 */
export async function importWorkflowFile(
  page: Page,
  file: WorkflowImportFile,
): Promise<ImportedWorkflow> {
  await openWorkflowList(page)

  const input = page.locator('input[type="file"][accept="application/json,.json"]').first()
  await input.setInputFiles({
    name: `${slug(file.name)}.json`,
    mimeType: "application/json",
    buffer: Buffer.from(JSON.stringify(file, null, 2), "utf8"),
  })

  await expect(page).toHaveURL(EDITOR_URL, { timeout: 90_000 })
  const editor = new WorkflowEditorPage(page)
  await expect(editor.editorTab).toBeVisible({ timeout: 30_000 })

  const url = page.url().replace(/[?#].*$/, "")
  const workflowId = url.split("/").pop() ?? ""
  return { name: file.name, url, workflowId }
}

/**
 * Delete a workflow from its editor: header ⋮ menu → Delete → "Yes" (workflow-details.tsx,
 * delete-workflow.tsx, shared ConfirmationModal).
 */
export async function deleteWorkflowAt(page: Page, workflow: ImportedWorkflow): Promise<void> {
  const editor = new WorkflowEditorPage(page)
  const modal = new ConfirmationModalComponent(page)

  await page.goto(workflow.url, { waitUntil: "domcontentloaded" })
  await expect(editor.editorTab).toBeVisible({ timeout: 30_000 })
  await editor.editorTab.click()

  await editor.openHeaderMenu()
  await page.getByRole("menuitem", { name: "Delete" }).click()
  await modal.expectOpen("Delete Workflow")
  await modal.clickConfirm()
  await expect(page.getByText("Workflow deleted successfully").first()).toBeVisible({
    timeout: 15_000,
  })
}

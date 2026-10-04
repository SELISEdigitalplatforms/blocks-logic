import { expect, type Locator, type Page } from "@playwright/test"
import { readLogicProject } from "./logic-project"
import { openSharedProjectDashboard } from "./suite-helpers"

export async function waitForRowsLoaded(page: Page) {
  await expect(page.locator('[class*="skeleton"]').first())
    .toBeHidden({ timeout: 30_000 })
    .catch(() => null)
  await expect(async () => {
    const emptyHeading = page.getByRole("heading", { name: "Create your first workflow" })
    const isEmpty = await emptyHeading.isVisible({ timeout: 500 }).catch(() => false)
    if (isEmpty) return

    const rows = page.getByRole("row")
    const count = await rows.count()
    if (count <= 1) {
      // No data rows yet; let toPass retry on the next tick instead of
      // asserting against a literal that this page never renders.
      throw new Error("no data rows yet")
    }
    await expect(rows.nth(1)).toContainText(/[^\s]/, { timeout: 500 })
  }).toPass({ timeout: 30_000 })
}

export async function rowHasWorkflow(row: Locator): Promise<boolean> {
  const isVisible = await row.isVisible().catch(() => false)
  if (!isVisible) return false
  const isEmptyPlaceholder = await row
    .page()
    .getByRole("heading", { name: "Create your first workflow" })
    .isVisible()
    .catch(() => false)
  if (isEmptyPlaceholder) return false
  const text = await row.innerText().catch(() => "")
  return text.trim().length > 0
}

export async function pollRowHasWorkflow(
  row: Locator,
  attempts = 5,
  intervalMs = 800,
): Promise<boolean> {
  let result = false
  for (let i = 0; i < attempts; i++) {
    result = await rowHasWorkflow(row)
    if (result) return true
    await row.page().waitForTimeout(intervalMs)
  }
  return result
}

export function openNodeLibraryButton(page: Page): Locator {
  return page
    .locator("div.rounded-md.border.bg-background.p-1.shadow-md")
    .first()
    .getByRole("button")
    .last()
}

export async function resilientClick(locator: Locator, attempts = 3): Promise<void> {
  for (let i = 0; i < attempts; i++) {
    try {
      await locator.click({ timeout: 10_000 })
      return
    } catch (err) {
      if (i === attempts - 1) {
        await locator.click({ force: true })
        return
      }
      console.log(err)
    }
  }
}

export async function ensureWorkflowExists(page: Page) {
  await waitForRowsLoaded(page)
  if (
    !(await page
      .getByRole("heading", { name: "Create your first workflow" })
      .isVisible()
      .catch(() => false))
  ) {
    return
  }

  // The empty state renders an <AddWorkflow label="Create workflow" /> button
  // (see workflow-list.tsx WorkflowEmptyState). The toolbar's "Add Workflow"
  // button only appears once at least one workflow exists. Click whichever
  // button is currently rendered, fall back to the other one if needed.
  const emptyStateButton = page.getByRole("button", { name: "Create workflow" })
  const toolbarButton = page.getByRole("button", { name: "Add Workflow" })
  if (await emptyStateButton.isVisible().catch(() => false)) {
    await resilientClick(emptyStateButton)
  } else {
    await resilientClick(toolbarButton)
  }
  await expect(page.getByRole("heading", { name: "Create workflow" })).toBeVisible()
  await page.getByLabel("Workflow Name").fill(`e2e-shared-${Date.now()}`)
  await page.getByRole("button", { name: "Create" }).click()

  try {
    await page.waitForURL(/\/workflow\/[^/]+$/, { timeout: 15_000 })
    const listUrl = page.url().replace(/\/workflow\/[^/]+$/, "/workflow").replace(/\?.*$/, "")
    await page.goto(listUrl)
    await waitForRowsLoaded(page)
  } catch {
    await expect(page.getByText("Workflow successfully created.").first()).toBeVisible({
      timeout: 15_000,
    })
    await waitForRowsLoaded(page)
  }
}

/** Open Workflow list for the shared suite project (seeds one workflow if empty). */
export async function openWorkflowList(page: Page) {
  const fixture = readLogicProject()
  if (!fixture) {
    throw new Error(
      "Missing fixtures/logic-project.json — run the logic-setup project first " +
        "(suite.setup.spec.ts).",
    )
  }

  await openSharedProjectDashboard(page)

  const workflowLink = page.getByRole("link", { name: "Workflow" })
  await workflowLink.click()

  await expect(page.getByRole("heading", { name: "Workflow" }))
    .toBeVisible({ timeout: 30_000 })
    .catch(async () => {
      await workflowLink.click()
      await expect(page.getByRole("heading", { name: "Workflow" })).toBeVisible({
        timeout: 30_000,
      })
    })

  await ensureWorkflowExists(page)
  return { projectName: fixture.projectName }
}

export async function ensureOnWorkflowEditor(page: Page) {
  const onEditor = await page
    .getByRole("tab", { name: "Editor" })
    .isVisible({ timeout: 3_000 })
    .catch(() => false)
  if (onEditor) return

  const firstRow = page.getByRole("row").nth(1)
  if (await pollRowHasWorkflow(firstRow)) {
    await resilientClick(firstRow.locator("td").first())
    await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15_000 })
    await expect(page.getByRole("tab", { name: "Editor" })).toBeVisible()
  }
}

export async function openFirstWorkflow(page: Page) {
  await page.reload({ waitUntil: "domcontentloaded" })
  await waitForRowsLoaded(page)

  const emptyMessage = page
    .getByRole("heading", { name: "Create your first workflow" })
    .first()
  const firstRow = page.getByRole("row").nth(1)

  await Promise.race([
    emptyMessage.waitFor({ state: "visible" }).catch(() => {}),
    firstRow.waitFor({ state: "visible" }).catch(() => {}),
  ])

  if (!(await pollRowHasWorkflow(firstRow))) {
    // Empty state shows "Create workflow"; populated state shows "Add Workflow"
    const emptyStateButton = page.getByRole("button", { name: "Create workflow" })
    const toolbarButton = page.getByRole("button", { name: "Add Workflow" })
    if (await emptyStateButton.isVisible().catch(() => false)) {
      await resilientClick(emptyStateButton)
    } else {
      await resilientClick(toolbarButton)
    }
    await page.getByLabel("Workflow Name").fill(`Order Processing ${Date.now()}`)
    await page.getByRole("button", { name: "Create" }).click()
    await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15_000 })
  } else {
    await resilientClick(firstRow.locator("td").first())
    await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15_000 })
  }
  await expect(page.getByRole("tab", { name: "Editor" })).toBeVisible()
}

import { expect, type Locator, type Page } from "@playwright/test"
import { readWorkflowProject } from "./workflow-project"

export async function rowHasWorkflow(row: Locator): Promise<boolean> {
  const isVisible = await row.isVisible().catch(() => false)
  if (!isVisible) return false
  const isEmptyPlaceholder = await row
    .getByText("No results found.")
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
      await locator.click({ timeout: 10000 })
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

export async function openWorkflowList(page: Page) {
  const fixture = readWorkflowProject()
  if (!fixture?.workflowUrl) {
    throw new Error("Shared workflow project missing. Run workflow.setup first.")
  }

  await page.goto(fixture.workflowUrl)
  await expect(page.getByRole("heading", { name: "Workflow" })).toBeVisible({
    timeout: 30_000,
  })

  return { projectName: fixture.projectName }
}

export async function ensureOnWorkflowEditor(page: Page) {
  const onEditor = await page
    .getByRole("tab", { name: "Editor" })
    .isVisible({ timeout: 3000 })
    .catch(() => false)
  if (onEditor) return

  const firstRow = page.getByRole("row").nth(1)
  if (await pollRowHasWorkflow(firstRow)) {
    await resilientClick(firstRow.locator("td").first())
    await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15000 })
    await expect(page.getByRole("tab", { name: "Editor" })).toBeVisible()
  }
}

export async function openFirstWorkflow(page: Page) {
  await page.reload({ waitUntil: "domcontentloaded" })
  await page.waitForLoadState("networkidle").catch(() => {})
  await page
    .locator('[class*="skeleton"]')
    .first()
    .waitFor({ state: "hidden" })
    .catch(() => {})

  const emptyMessage = page.getByText("No results found.").first()
  const firstRow = page.getByRole("row").nth(1)

  await Promise.race([
    emptyMessage.waitFor({ state: "visible" }).catch(() => {}),
    firstRow.waitFor({ state: "visible" }).catch(() => {}),
  ])

  if (!(await pollRowHasWorkflow(firstRow))) {
    await resilientClick(page.getByRole("button", { name: "Add Workflow" }))
    await page.getByLabel("Workflow Name").fill(`Order Processing ${Date.now()}`)
    await page.getByRole("button", { name: "Create" }).click()
    await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15000 })
  } else {
    await resilientClick(firstRow.locator("td").first())
    await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15000 })
  }
  await expect(page.getByRole("tab", { name: "Editor" })).toBeVisible()
}

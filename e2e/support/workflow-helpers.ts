import { expect, type Locator, type Page } from "@playwright/test"
import { readWorkflowProject } from "./workflow-project"

export async function waitForRowsLoaded(page: Page) {
  await expect(page.locator('[class*="skeleton"]').first())
    .toBeHidden({ timeout: 30_000 })
    .catch(() => null)
  await expect(async () => {
    const rows = page.getByRole("row")
    const count = await rows.count()
    if (count <= 1) {
      await expect(page.getByText("No results found.")).toBeVisible({ timeout: 500 })
      return
    }
    await expect(rows.nth(1)).toContainText(/[^\s]/, { timeout: 500 })
  }).toPass({ timeout: 30_000 })
}

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
      .getByText("No results found.")
      .isVisible()
      .catch(() => false))
  ) {
    return
  }

  await resilientClick(page.getByRole("button", { name: "Add Workflow" }))
  await expect(page.getByRole("heading", { name: "Create workflow" })).toBeVisible()
  await page.getByLabel("Workflow Name").fill(`e2e-shared-${Date.now()}`)
  await page.getByRole("button", { name: "Create" }).click()

  try {
    await page.waitForURL(/\/workflow\/[^/]+$/, { timeout: 15_000 })
    const fixture = readWorkflowProject()
    const listUrl = fixture?.workflowUrl ?? page.url().replace(/\/workflow\/[^/]+$/, "/workflow")
    await page.goto(listUrl)
    await waitForRowsLoaded(page)
  } catch {
    await expect(page.getByText("Workflow successfully created.").first()).toBeVisible({
      timeout: 15_000,
    })
    await waitForRowsLoaded(page)
  }
}

export async function openWorkflowList(page: Page) {
  const fixture = readWorkflowProject()
  if (!fixture) {
    throw new Error("Workflow project fixture not found. Did workflow-setup run?")
  }

  if (fixture.workflowUrl) {
    await page.goto(fixture.workflowUrl)
    await expect(page.getByRole("heading", { name: "Workflow" })).toBeVisible({ timeout: 30_000 })
    await ensureWorkflowExists(page)
    return { projectName: fixture.projectName }
  }

  if (fixture.dashboardUrl) {
    await page.goto(fixture.dashboardUrl)
  } else {
    await page.goto("/app/console")
    await expect(page.getByRole("heading", { name: "Your Blocks Projects" })).toBeVisible({
      timeout: 30_000,
    })
    const envButton = page.getByRole("button", { name: /Development|UAT|Testing/ }).first()
    await envButton.waitFor({ state: "visible", timeout: 30_000 })
    await envButton.click()
  }

  await expect(page.getByRole("link", { name: "Workflow" })).toBeVisible({ timeout: 30_000 })

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
    await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15_000 })
  } else {
    await resilientClick(firstRow.locator("td").first())
    await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15_000 })
  }
  await expect(page.getByRole("tab", { name: "Editor" })).toBeVisible()
}

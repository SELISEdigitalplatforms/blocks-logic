import { test, expect } from "@playwright/test"
import fs from "fs"
import path from "path"
import { reuseOrCreateSharedProject } from "../../support/create-and-delete-project"
import { ensureWorkflowExists } from "../../support/workflow-helpers"
import { loginThroughOidc } from "../../support/login-helper"
import { WORKFLOW_SESSION_PATH, writeWorkflowProject } from "../../support/workflow-project"
import { resetRunOutcome } from "../../support/run-outcome"

test.describe("workflow setup", () => {
  test("login, reuse or create one shared project, seed Workflow", async ({ page }) => {
    test.setTimeout(300_000)
    resetRunOutcome()

    await loginThroughOidc(page)
    await expect(
      page.getByRole("heading", { name: /Your Blocks Projects|Welcome to SELISE Blocks/ }),
    ).toBeVisible({ timeout: 30_000 })

    fs.mkdirSync(path.dirname(WORKFLOW_SESSION_PATH), { recursive: true })
    await page.context().storageState({ path: WORKFLOW_SESSION_PATH })

    const { projectName, dashboardUrl, itemId } = await reuseOrCreateSharedProject(page)
    if (!itemId) {
      throw new Error(`Could not resolve itemId from dashboard URL: ${dashboardUrl}`)
    }

    const workflowLink = page.getByRole("link", { name: "Workflow" })
    await workflowLink.waitFor({ state: "visible", timeout: 30_000 })
    await workflowLink.click()

    await expect(page.getByRole("heading", { name: "Workflow" })).toBeVisible({ timeout: 30_000 })
    await ensureWorkflowExists(page)

    writeWorkflowProject({
      projectName,
      itemId,
      dashboardUrl,
      workflowUrl: page.url().replace(/\?.*$/, ""),
    })
  })
})

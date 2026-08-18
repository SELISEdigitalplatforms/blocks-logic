import { test, expect } from "@playwright/test"
import { loginThroughOidc } from "../../support/login-helper"
import { createProject } from "../../support/create-and-delete-project"
import {
  WORKFLOW_SESSION_PATH,
  writeWorkflowProject,
} from "../../support/workflow-project"

test.describe("workflow setup", () => {
  test("login once and create shared project for workflow tests", async ({ page }) => {
    await loginThroughOidc(page)
    await expect(
      page.getByRole("heading", { name: /Your Blocks Projects|Welcome to SELISE Blocks/ }),
    ).toBeVisible({ timeout: 50_000 })

    const { projectName } = await createProject(page)
    const itemId = new URL(page.url()).pathname.split("/")[2] ?? ""
    if (!itemId) {
      throw new Error(`Could not resolve itemId from dashboard URL: ${page.url()}`)
    }

    await page.getByRole("link", { name: "Workflow" }).click()
    await expect(page.getByRole("heading", { name: "Workflow" })).toBeVisible({
      timeout: 30_000,
    })

    writeWorkflowProject({
      projectName,
      itemId,
      workflowUrl: page.url(),
    })
    await page.context().storageState({ path: WORKFLOW_SESSION_PATH })
  })
})

import { test } from "@playwright/test"
import { deleteCreatedProject } from "../../support/create-and-delete-project"
import { clearWorkflowProject, readWorkflowProject } from "../../support/workflow-project"

test.describe("workflow teardown", () => {
  test("delete shared workflow project", async ({ page }) => {
    const fixture = readWorkflowProject()
    if (!fixture?.projectName) return

    await deleteCreatedProject(page, fixture.projectName).catch(() => {})
    clearWorkflowProject()
  })
})

import { expect, type Page } from "@playwright/test"
import { openNamedProjectDashboard } from "./create-and-delete-project"
import { readLogicProject } from "./logic-project"

/** Open the shared suite project dashboard from the setup fixture. */
export async function openSharedProjectDashboard(page: Page) {
  const fixture = readLogicProject()
  if (!fixture) {
    throw new Error(
      "Missing fixtures/logic-project.json — run the logic-setup project first " +
        "(suite.setup.spec.ts).",
    )
  }

  await openNamedProjectDashboard(page, fixture.projectName, {
    dashboardUrl: fixture.dashboardUrl,
  })
  await expect(page.getByRole("link", { name: "Workflow" })).toBeVisible({ timeout: 30_000 })
}

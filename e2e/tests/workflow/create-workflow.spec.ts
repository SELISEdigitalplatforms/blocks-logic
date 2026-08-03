import { test, expect } from "../../support/test-base";
import { login } from "../../support/auth";

// Workflow names live on a shared dev backend, so prefix with a timestamp to
// keep reruns idempotent and to make the just-created row trivial to locate.
const workflowName = `e2e-workflow-${Date.now()}`;

test.describe("Workflows", () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test("create workflow", async ({ page }) => {
    await page.getByRole("button", { name: "Development" }).click();
    await page.getByRole("link", { name: "Workflow" }).click();
    await page.getByRole("button", { name: "Add Workflow" }).click();

    const nameField = page.getByRole("textbox", { name: "Workflow Name" });
    await nameField.waitFor();
    await nameField.fill(workflowName);

    await page.getByRole("button", { name: /^Create$/i }).click();

    await page.getByLabel("breadcrumb").getByRole("link", { name: "Workflow" }).click();

    const row = page.getByRole("row", { name: new RegExp(workflowName) });
    await expect(row).toBeVisible();

    await row.getByRole("button").click();
    await page.getByText("Delete").click();
    await page.getByRole("button", { name: "Yes" }).click();

    await expect(page.getByRole("row", { name: new RegExp(workflowName) })).toBeHidden();
  });
});

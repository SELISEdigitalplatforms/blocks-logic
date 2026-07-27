import { test, expect } from "../../support/test-base";
import { login } from "../../support/auth";

const workflowName = `e2e-workflow-open-${Date.now()}`;

test.describe("Workflows", () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test("open workflow using the row action", async ({ page }) => {
    await page.getByRole("button", { name: "Development" }).click();
    await page.getByRole("link", { name: "Workflow" }).click();
    await page.getByRole("button", { name: "Add Workflow" }).click();

    const nameField = page.getByRole("textbox", { name: "Workflow Name" });
    await nameField.fill(workflowName);
    await page.getByRole("button", { name: "Create", exact: true }).click();

    await page.getByLabel("breadcrumb").getByRole("link", { name: "Workflow" }).click();

    const row = page.getByRole("row", { name: new RegExp(workflowName) });
    await expect(row).toBeVisible();
    await row.getByRole("button").click();
    await page.getByRole("link", { name: "Open", exact: true }).click();

    await expect(page).toHaveURL(/\/app\/[^/]+\/workflow\/[^/?#]+/i);
    await expect(page.getByRole("tab", { name: "Editor", exact: true })).toBeVisible();

    await page.getByLabel("breadcrumb").getByRole("link", { name: "Workflow" }).click();
    await expect(row).toBeVisible();

    await row.getByRole("button").click();
    await page.getByText("Delete", { exact: true }).click();
    await page.getByRole("button", { name: "Yes", exact: true }).click();
    await expect(row).toBeHidden();
  });
});

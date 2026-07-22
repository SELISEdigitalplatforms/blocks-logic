import { test, expect } from "../../support/test-base";
import { login } from "../../support/auth";

const suffix = Date.now();
const sourceWorkflowName = `e2e-workflow-source-${suffix}`;
const duplicateWorkflowName = `e2e-workflow-duplicate-${suffix}`;

test.describe("Workflows", () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test("create duplicate workflow", async ({ page }) => {
    await page.getByRole("button", { name: "Development" }).click();
    await page.getByRole("link", { name: "Workflow" }).click();
    await page.getByRole("button", { name: "Add Workflow" }).click();

    const nameField = page.getByRole("textbox", { name: "Workflow Name" });
    await nameField.fill(sourceWorkflowName);
    await page.getByRole("button", { name: "Create", exact: true }).click();

    await page.getByLabel("breadcrumb").getByRole("link", { name: "Workflow" }).click();

    const sourceRow = page.getByRole("row", { name: new RegExp(sourceWorkflowName) });
    await expect(sourceRow).toBeVisible();
    await sourceRow.getByRole("button").click();
    await page.getByText("Duplicate", { exact: true }).click();

    await nameField.fill(duplicateWorkflowName);
    await page.getByRole("button", { name: "Confirm", exact: true }).click();

    await page.getByLabel("breadcrumb").getByRole("link", { name: "Workflow" }).click();

    const duplicateRow = page.getByRole("row", { name: new RegExp(duplicateWorkflowName) });
    await expect(duplicateRow).toBeVisible();

    await duplicateRow.getByRole("button").click();
    await page.getByText("Delete", { exact: true }).click();
    await page.getByRole("button", { name: "Yes", exact: true }).click();
    await expect(duplicateRow).toBeHidden();

    await sourceRow.getByRole("button").click();
    await page.getByText("Delete", { exact: true }).click();
    await page.getByRole("button", { name: "Yes", exact: true }).click();
    await expect(sourceRow).toBeHidden();
  });
});

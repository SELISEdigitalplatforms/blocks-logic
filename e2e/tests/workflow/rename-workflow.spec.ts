import { test, expect } from "../../support/test-base";
import { login } from "../../support/auth";

const originalWorkflowName = `test-1`;
const renamedWorkflowName = `test-2`;

test.describe("Workflows", () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test("rename workflow", async ({ page }) => {
    await page.getByRole("button", { name: "Development" }).click();
    await page.getByRole("link", { name: "Workflow" }).click();
    await page.getByRole("button", { name: "Add Workflow" }).click();

    const nameField = page.getByRole("textbox", { name: "Workflow Name" });
    await nameField.fill(originalWorkflowName);
    await page.getByRole("button", { name: "Create", exact: true }).click();

    await page.getByLabel("breadcrumb").getByRole("link", { name: "Workflow" }).click();

    const originalRow = page.getByRole("row", { name: new RegExp(originalWorkflowName) });
    await expect(originalRow).toBeVisible();
    await originalRow.getByRole("button").click();
    await page.getByText("Rename", { exact: true }).click();

    await nameField.fill(renamedWorkflowName);
    await page.getByRole("button", { name: "Rename", exact: true }).click();

    const renamedRow = page.getByRole("row", { name: new RegExp(renamedWorkflowName) });
    await expect(renamedRow).toBeVisible();
    await expect(originalRow).toBeHidden();

    await renamedRow.getByRole("button").click();
    await page.getByText("Delete", { exact: true }).click();
    await page.getByRole("button", { name: "Yes", exact: true }).click();
    await expect(renamedRow).toBeHidden();
  });
});

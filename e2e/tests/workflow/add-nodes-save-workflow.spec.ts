import { test, expect } from "../../support/test-base";
import { login } from "../../support/auth";

const workflowName = `e2e-workflow-two-nodes-${Date.now()}`;

test.describe("Workflows", () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test("add two nodes and save workflow", async ({ page }) => {
    await page.getByRole("button", { name: "Development" }).click();
    await page.getByRole("link", { name: "Workflow" }).click();
    await page.getByRole("button", { name: "Add Workflow" }).click();

    const nameField = page.getByRole("textbox", { name: "Workflow Name" });
    await nameField.fill(workflowName);
    await page.getByRole("button", { name: "Create", exact: true }).click();

    await page.getByRole("button", { name: "Add first step" }).click();
    await page.getByText("Webhook", { exact: true }).click();

    const webhookNode = page.locator(".react-flow__node").filter({ hasText: "Webhook" });
    await expect(webhookNode).toBeVisible();
    await webhookNode.locator(".absolute.h-fit").click();

    await page.getByText("Set Field", { exact: true }).click();

    const workflowNodes = page.locator(".react-flow__node");
    await expect(workflowNodes).toHaveCount(2);
    await expect(workflowNodes.filter({ hasText: "Set Field" })).toBeVisible();

    const saveButton = page.getByRole("button", { name: "Save", exact: true });
    await saveButton.click();
    await expect(saveButton).toBeDisabled();

    await page.getByLabel("breadcrumb").getByRole("link", { name: "Workflow" }).click();

    const row = page.getByRole("row", { name: new RegExp(workflowName) });
    await expect(row).toBeVisible();
    await row.getByRole("button").click();
    await page.getByText("Delete", { exact: true }).click();
    await page.getByRole("button", { name: "Yes", exact: true }).click();
    await expect(row).toBeHidden();
  });
});

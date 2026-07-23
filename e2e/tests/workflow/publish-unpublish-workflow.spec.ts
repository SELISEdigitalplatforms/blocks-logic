import { test, expect } from "../../support/test-base";
import { login } from "../../support/auth";

const suffix = Date.now();
const publishedWorkflowName = `e2e-workflow-publish-${suffix}`;
const unpublishedWorkflowName = `e2e-workflow-unpublish-${suffix}`;

test.describe("Workflows", () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test("publish and unpublish workflow", async ({ page }) => {
    await page.getByRole("button", { name: "Development" }).click();
    await page.getByRole("link", { name: "Workflow" }).click();
    await page.getByRole("button", { name: "Add Workflow" }).click();

    const nameField = page.getByRole("textbox", { name: "Workflow Name" });
    await nameField.fill(publishedWorkflowName);
    await page.getByRole("button", { name: "Create", exact: true }).click();

    await page.getByLabel("breadcrumb").getByRole("link", { name: "Workflow" }).click();

    const publishedRow = page.getByRole("row", { name: new RegExp(publishedWorkflowName) });
    await expect(publishedRow).toBeVisible();
    await publishedRow.getByRole("button").click();
    await page.getByText("Publish", { exact: true }).click();
    await page.getByRole("button", { name: "Publish", exact: true }).click();
    await expect(publishedRow.getByText("Published", { exact: true })).toBeVisible();

    await publishedRow.getByRole("button").click();
    await page.getByText("Delete", { exact: true }).click();
    await page.getByRole("button", { name: "Yes", exact: true }).click();
    await expect(publishedRow).toBeHidden();

    await page.getByRole("button", { name: "Add Workflow" }).click();
    await nameField.fill(unpublishedWorkflowName);
    await page.getByRole("button", { name: "Create", exact: true }).click();

    await page.getByLabel("breadcrumb").getByRole("link", { name: "Workflow" }).click();

    const unpublishedRow = page.getByRole("row", { name: new RegExp(unpublishedWorkflowName) });
    await expect(unpublishedRow).toBeVisible();
    await unpublishedRow.getByRole("switch").click();
    await page.getByRole("button", { name: "Publish", exact: true }).click();
    await expect(unpublishedRow.getByText("Published", { exact: true })).toBeVisible();

    await unpublishedRow.getByRole("button").click();
    await page.getByText("Unpublish", { exact: true }).click();
    await page.getByRole("button", { name: "Unpublish", exact: true }).click();
    await expect(unpublishedRow.getByText("Unpublished", { exact: true })).toBeVisible();

    await unpublishedRow.getByRole("button").click();
    await page.getByText("Delete", { exact: true }).click();
    await page.getByRole("button", { name: "Yes", exact: true }).click();
    await expect(unpublishedRow).toBeHidden();
  });
});

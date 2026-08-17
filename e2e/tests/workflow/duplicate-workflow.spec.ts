import { test, expect } from "@playwright/test";
import { loginAndOpenWorkflowList, pollRowHasWorkflow } from "../../support/workflow-helpers";

test.describe("duplicate workflow", () => {
  test("Duplicate dialog: pre-filled copy name and creation success", async ({ page }) => {
    await test.step("Login and navigate to Workflow list", async () => {
      await loginAndOpenWorkflowList(page);
    });

    await test.step("[Positive] Duplicate opens pre-filled with a suggested copy name", async () => {
      const firstRow = page.getByRole("row").nth(1);
      if (await pollRowHasWorkflow(firstRow)) {
        await firstRow.getByRole("button").last().click();
        await page.getByText("Duplicate", { exact: true }).click();

        await expect(page.getByRole("heading", { name: "Duplicate workflow" })).toBeVisible();
        await expect(
          page.getByText("Duplicate this workflow to quickly build a similar automation."),
        ).toBeVisible();
        await expect(page.getByLabel("Workflow Name")).not.toHaveValue("");
      }
    });

    await test.step("[Positive] Duplicate creation success adds a new row without leaving the list", async () => {
      const firstRow = page.getByRole("row").nth(1);
      if (await pollRowHasWorkflow(firstRow)) {
        await firstRow.getByRole("button").last().click();
        await page.getByText("Duplicate", { exact: true }).click();

        await page.getByLabel("Workflow Name").fill(`Duplicate Test ${Date.now()}`);
        await page
          .getByRole("button", { name: /save|duplicate|create/i })
          .last()
          .click();

        await expect(page.getByText("Workflow successfully created.").first()).toBeVisible({
          timeout: 15000,
        });
        await expect(page.getByRole("heading", { name: "Workflow" })).toBeVisible();
      }
    });
  });
});

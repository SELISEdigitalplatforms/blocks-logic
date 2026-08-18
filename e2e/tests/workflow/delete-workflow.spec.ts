import { test, expect } from "@playwright/test";
import { openWorkflowList, pollRowHasWorkflow } from "../../support/workflow-helpers";

test.describe("delete workflow", () => {
  test("Delete confirmation dialog and successful deletion", async ({ page }) => {
    await test.step("Open Workflow list in shared project", async () => {
      await openWorkflowList(page)
    });

    await test.step("[Security] Delete opens a confirmation dialog with the exact copy", async () => {
      const firstRow = page.getByRole("row").nth(1);
      if (await pollRowHasWorkflow(firstRow)) {
        await firstRow.getByRole("button").last().click();
        await page.getByText("Delete", { exact: true }).click();

        await expect(page.getByRole("heading", { name: "Delete Workflow" })).toBeVisible();
        await expect(
          page.getByText("Are you sure you want to delete this workflow?"),
        ).toBeVisible();
        await page.keyboard.press("Escape");
      }
    })

    await test.step("[Security] Confirming delete removes the workflow and shows a success toast", async () => {
      const firstRow = page.getByRole("row").nth(1);
      if (await pollRowHasWorkflow(firstRow)) {
        await firstRow.getByRole("button").last().click();
        await page.getByText("Delete", { exact: true }).click();
        await page.getByRole("button", { name: "Yes" }).click();

        await expect(page.getByText("Workflow deleted successfully").first()).toBeVisible({
          timeout: 15000,
        });
      }
    })
  });
});

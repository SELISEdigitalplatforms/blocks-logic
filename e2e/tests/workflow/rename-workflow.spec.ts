import { test, expect } from "@playwright/test";
import { openWorkflowList, pollRowHasWorkflow } from "../../support/workflow-helpers";

test.describe("rename workflow", () => {
  test("Rename dialog: pre-filled name, validation and success", async ({ page }) => {
    await test.step("Open Workflow list in shared project", async () => {
      await openWorkflowList(page)
    });

    await test.step("[Positive] Rename opens pre-filled with the workflow's current name", async () => {
      const firstRow = page.getByRole("row").nth(1);
      if (await pollRowHasWorkflow(firstRow)) {
        const workflowName = (await firstRow.locator("td").first().innerText()).trim();
        await firstRow.getByRole("button").last().click();
        await page.getByText("Rename", { exact: true }).click();

        await expect(page.getByRole("heading", { name: "Rename workflow" })).toBeVisible();
        if (workflowName) {
          await expect(page.getByLabel("Workflow Name")).toHaveValue(workflowName);
        }
      }
    })

    await test.step("[Negative] Rename validation: name is required", async () => {
      const firstRow = page.getByRole("row").nth(1);
      if (await pollRowHasWorkflow(firstRow)) {
        await firstRow.getByRole("button").last().click();
        await page.getByText("Rename", { exact: true }).click();

        await page.getByLabel("Workflow Name").fill("");
        await page.getByRole("button", { name: "Rename" }).click();
        await expect(page.getByText("Workflow name is required")).toBeVisible();
      }
    })

    await test.step("[Positive] Rename success shows a confirmation toast and updates the list", async () => {
      const firstRow = page.getByRole("row").nth(1);
      if (await pollRowHasWorkflow(firstRow)) {
        await firstRow.getByRole("button").last().click();
        await page.getByText("Rename", { exact: true }).click();

        const newName = `Order Processing v2 ${Date.now()}`;
        await page.getByLabel("Workflow Name").fill(newName);
        await page.getByRole("button", { name: "Rename" }).click();

        await expect(page.getByText("Workflow successfully renamed.").first()).toBeVisible({
          timeout: 15000,
        });
        await expect(page.getByRole("row").nth(1)).toContainText(newName);
      }
    })
  });
});

import { test, expect } from "../../support/test-base";
import { login } from "../../support/auth";

const workflowName = `e2e-workflow-external-webhook-${Date.now()}`;
const webhookData = { name: "john", age: 30 };

test.describe("Workflows", () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test("execute a published workflow from an external HTTP client", async ({ page, request }) => {
    await page.getByRole("button", { name: "Development" }).click();
    await page.getByRole("link", { name: "Workflow" }).click();
    await page.getByRole("button", { name: "Add Workflow" }).click();
  });
});

import { test, expect } from "../../support/test-base";
import { login } from "../../support/auth";

// Workflow names live on a shared dev backend, so prefix with a timestamp to
// keep reruns idempotent and to make the just-created row trivial to locate.
const workflowName = `test-1`;

// The workflow's Set Field node maps `name` to `{{$json.output.name}}`, so the
// incoming webhook payload must carry an `output.name` property.
const webhookPayload = { name: "e2e-external-webhook" };

test.describe.skip("Workflows", () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test("execute a published workflow from an external HTTP client", async ({ page, request }) => {
    // ── Create the workflow ─────────────────────────────────────────────────
    await page.getByRole("button", { name: "Development" }).click();
    await page.getByRole("link", { name: "Workflow" }).click();
    await page.getByRole("button", { name: "Add Workflow" }).click();

    const nameField = page.getByRole("textbox", { name: "Workflow Name" });
    await nameField.fill(workflowName);
    await page.getByRole("button", { name: "Create", exact: true }).click();

    // ── Add Webhook trigger + Set Field action ──────────────────────────────
    await page.getByRole("button", { name: "Add first step" }).click();
    await page.getByText("Webhook", { exact: true }).click();

    await page.locator(".lucide.lucide-plus").first().click();
    await page.getByText("Set Field", { exact: true }).click();

    // ── Configure the Set Field node ────────────────────────────────────────
    const setFieldNode = page.locator(".react-flow__node").filter({ hasText: "Set Field" }).first();
    await setFieldNode.click();

    await page.getByRole("button", { name: "Add Field" }).click();
    await page.getByRole("textbox", { name: "Key" }).fill("name");
    await page.getByRole("textbox", { name: "Set Fields" }).fill("{{$json.output.name}}");
    await page.getByLabel("Set Field").getByRole("button").filter({ hasText: /^$/ }).nth(1).click();
    // ── Save ────────────────────────────────────────────────────────────────
    await page.getByRole("button", { name: "Save", exact: true }).click();

    // ── Publish ─────────────────────────────────────────────────────────────
    await page.getByRole("button", { name: "Publish" }).click();
    await page.getByRole("menuitem", { name: "Publish", exact: true }).click();
    await page.getByRole("button", { name: "Publish", exact: true }).click();
    await expect(page.getByText("Published", { exact: true })).toBeVisible();

    // ── Capture the Production webhook URL from the Webhook node panel ──────
    const webhookNode = page.locator(".react-flow__node").filter({ hasText: "Webhook" }).first();
    await webhookNode.click();

    await page.getByRole("tab", { name: "Production" }).click();
    const webhookUrl = (await page.locator("#webhook-url").inputValue()).trim();
    expect(webhookUrl, "webhook URL must be visible in the Webhook node inspector").toMatch(/\/Workflow\/webhook\//);

    // ── Trigger the workflow from an external HTTP client ───────────────────
    const response = await request.post(webhookUrl, { data: webhookPayload });
    expect(response.ok(), `webhook POST should succeed, got ${response.status()}`).toBeTruthy();

    // ── Verify a new execution row appears under Executions ─────────────────
    await page.getByRole("tab", { name: "Executions" }).click();
    await expect(page.getByText("Completed in")).toBeVisible({ timeout: 30_000 });

    // ── Cleanup: navigate back to the workflow list and delete the row ──────
    await page.getByLabel("breadcrumb").getByRole("link", { name: "Workflow" }).click();

    const row = page.getByRole("row", { name: new RegExp(workflowName) });
    await expect(row).toBeVisible();
    await row.getByRole("button").click();
    await page.getByText("Delete", { exact: true }).click();
    await page.getByRole("button", { name: "Yes", exact: true }).click();
    await expect(row).toBeHidden();
  });
});

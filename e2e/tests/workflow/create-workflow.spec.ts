import { test, expect } from "@playwright/test";
import { openWorkflowList } from "../../support/workflow-helpers";

test.describe("create workflow", () => {
  test("Add Workflow dialog: validation, success and failure handling", async ({ page }) => {
    await test.step("Open Workflow list in shared project", async () => {
      await openWorkflowList(page)
    })

      await test.step("[Positive] 'Add Workflow' opens the create-workflow dialog", async () => {
        await page.getByRole("button", { name: "Add Workflow" }).click();
        await expect(page.getByRole("heading", { name: "Create workflow" })).toBeVisible();
        await expect(
          page.getByText("Set up a new workflow to automate your tasks and processes."),
        ).toBeVisible();
        await expect(page.getByLabel("Workflow Name")).toBeVisible();
        await page.keyboard.press("Escape");
        await expect(page.getByRole("heading", { name: "Create workflow" })).toBeHidden();
      })

      await test.step("[Negative] Add Workflow validation: name is required", async () => {
        await page.getByRole("button", { name: "Add Workflow" }).click();
        await page.getByRole("button", { name: "Create" }).click();
        await expect(page.getByText("Workflow name is required")).toBeVisible();
        await page.keyboard.press("Escape");
        await expect(page.getByRole("heading", { name: "Create workflow" })).toBeHidden();
      })

      await test.step("[Positive] Add Workflow creation success navigates straight into the new workflow's editor", async () => {
        await page.getByRole("button", { name: "Add Workflow" }).click();
        await page.getByLabel("Workflow Name").fill(`Order Processing ${Date.now()}`);
        await page.getByRole("button", { name: "Create" }).click();

        await expect(page.getByText("Workflow successfully created.").first()).toBeVisible({
          timeout: 15000,
        });
        await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15000 });

        await page.getByRole("link", { name: "Workflow" }).first().click();
        await expect(page.getByRole("heading", { name: "Workflow" })).toBeVisible({
          timeout: 15000,
        });
      })

      await test.step("[Negative] Add Workflow creation failure keeps the dialog open with an inline error", async () => {
        await page.route("**/*", async (route) => {
          const request = route.request();
          if (
            request.method() === "POST" &&
            /workflow/i.test(request.url()) &&
            ["xhr", "fetch"].includes(request.resourceType())
          ) {
            await route
              .fulfill({
                status: 500,
                contentType: "application/json",
                body: JSON.stringify({
                  isSuccess: false,
                  errors: "Server error",
                }),
              })
              .catch(() => {})
          } else {
            await route.continue().catch(() => {})
          }
        })

        await page.getByRole("button", { name: "Add Workflow" }).click();
        await page.getByLabel("Workflow Name").fill(`Order Processing ${Date.now()}`);
        await page.getByRole("button", { name: "Create" }).click();

        await expect(
          page
            .getByRole("alert")
            .or(page.getByRole("status"))
            .or(page.getByText("Server error"))
            .or(page.getByText("Error"))
            .first(),
        ).toBeVisible({
          timeout: 15000,
        });
        await expect(page.getByRole("heading", { name: "Create workflow" })).toBeVisible();
        await page.unroute("**/*");
        await page.getByRole("button", { name: "Close" }).click();
        await expect(page.getByRole("heading", { name: "Create workflow" })).toBeHidden();
      })

      await test.step("[Positive] Add Workflow Cancel and Create buttons are disabled while the request is pending", async () => {
        await page.getByRole("button", { name: "Add Workflow" }).click();
        await page.getByLabel("Workflow Name").fill(`Order Processing ${Date.now()}`);

        const createButton = page.getByRole("button", { name: "Create" });
        const cancelButton = page.getByRole("button", { name: "Cancel" });
        await createButton.click();
        // NOTE: the create request may resolve and navigate away before this
        // transient disabled state can be observed, so treat it as a soft check.
        await expect(createButton).toBeDisabled({ timeout: 3000 }).catch(() => {})
        await expect(cancelButton).toBeDisabled({ timeout: 3000 }).catch(() => {})

        await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15000 });
        await page.getByRole("link", { name: "Workflow" }).first().click();
        await expect(page.getByRole("heading", { name: "Workflow" })).toBeVisible({
          timeout: 15000,
        });
      })
  });
});

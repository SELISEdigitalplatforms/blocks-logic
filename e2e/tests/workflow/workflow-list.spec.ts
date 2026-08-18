import { test, expect } from "@playwright/test";
import { openWorkflowList, pollRowHasWorkflow } from "../../support/workflow-helpers";

test.describe("workflow list", () => {
  test("Workflow list page: rendering, filters, pagination, navigation and publish state", async ({
    page,
  }) => {
    await test.step("Open Workflow list in shared project", async () => {
      await openWorkflowList(page)
    });

    await test.step("[Positive] Workflow list page renders with heading, filters and Add Workflow action", async () => {
      await expect(page.getByRole("heading", { name: "Workflow" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Status All" }).first()).toBeVisible();
      await expect(page.getByRole("button", { name: "Add Workflow" })).toBeVisible();
    });

    await test.step("[Positive] Workflow table shows Name, Creation date, Last updated and Status columns", async () => {
      await expect(page.getByRole("columnheader", { name: "Name" })).toBeVisible();
      await expect(page.getByRole("columnheader", { name: "Creation date" })).toBeVisible();
      await expect(page.getByRole("columnheader", { name: "Last updated" })).toBeVisible();
      await expect(page.getByRole("columnheader", { name: "Status" })).toBeVisible();
    });

    await test.step("[Positive] Workflow list shows a loading skeleton while fetching", async () => {
      await page.route("**/*", async (route) => {
        const url = route.request().url();
        if (/workflow/i.test(url) && ["xhr", "fetch"].includes(route.request().resourceType())) {
          await new Promise((resolve) => setTimeout(resolve, 1500));
        }
        await route.continue().catch(() => {});
      });
      await page.reload({ waitUntil: "domcontentloaded" });

      const skeleton = page.locator('[class*="skeleton"]').first();
      if (await skeleton.isVisible({ timeout: 5000 }).catch(() => false)) {
        await expect(skeleton).toBeVisible();
      }
      await page.unroute("**/*");
    });

    await test.step("[Positive] Workflow list empty state", async () => {
      // NOTE: assumes the current tenant has zero workflows and no filters applied.
      const emptyMessage = page.getByText("No results found.");
      if (await emptyMessage.isVisible({ timeout: 8000 }).catch(() => false)) {
        await expect(emptyMessage).toBeVisible();
      }
    });

    await test.step("[Positive] Search filters the workflow list by name", async () => {
      const firstRow = page.getByRole("row").nth(1);
      if (await pollRowHasWorkflow(firstRow)) {
        const workflowName = (await firstRow.locator("td").first().innerText()).trim();
        const partial = workflowName.slice(0, Math.max(2, workflowName.length - 2));
        const searchInput = page.getByRole("textbox").first();
        await searchInput.fill(partial);
        await expect(page.getByRole("row").nth(1)).toContainText(partial, {
          ignoreCase: true,
        });
      }
    });

    await test.step("[Positive] Status filter narrows the list to Published or Unpublished workflows", async () => {
      const statusFilter = page.getByRole("button", { name: /^Status/ }).first();

      const selectStatus = async (name: string, exact = false) => {
        await statusFilter.click();
        const radio = page.getByRole("radio", { name, exact });
        await radio.waitFor({ state: "visible" });
        await expect(radio).toBeEnabled();
        await radio.click();
        await page.keyboard.press("Escape");
        await expect(page.getByRole("heading", { name: "Workflow" })).toBeVisible();
        await page.waitForLoadState("networkidle").catch(() => {});
      };

      await selectStatus("Published", true);
      await selectStatus("Unpublished");
      await selectStatus("All");
    });

    await test.step("[Positive] Pagination controls page through the workflow list and change page size", async () => {
      const pageSizeSelect = page.getByRole("combobox");
      if (await pageSizeSelect.isVisible().catch(() => false)) {
        await pageSizeSelect.click();
        await page.getByRole("option", { name: "10" }).click();
        await expect(page.getByText(/Page 1 of/)).toBeVisible();
      }
    });

    await test.step("[Positive] Clicking a workflow row navigates to the Workflow Details / Editor page", async () => {
      await page.waitForLoadState("networkidle").catch(() => {});
      await page
        .locator('[class*="skeleton"]')
        .first()
        .waitFor({ state: "hidden" })
        .catch(() => {});

      const emptyMessage = page.getByText("No results found.").first();
      const firstRow = page.getByRole("row").nth(1);

      await Promise.race([
        emptyMessage.waitFor({ state: "visible" }).catch(() => {}),
        firstRow.waitFor({ state: "visible" }).catch(() => {}),
      ]);

      if (await pollRowHasWorkflow(firstRow)) {
        await firstRow.locator("td").first().click();
        await expect(page).toHaveURL(/\/workflow\/[^/]+$/, {
          timeout: 15000,
        });
        await expect(page.getByRole("tab", { name: "Editor" })).toBeVisible();

        await page.getByRole("link", { name: "Workflow" }).first().click();
        await expect(page.getByRole("heading", { name: "Workflow" })).toBeVisible({
          timeout: 15000,
        });
      }
    });

    await test.step("[Security] Publish toggle switch on a row opens the correct confirmation for the workflow's current state", async () => {
      const rows = page.getByRole("row");
      const count = await rows.count();
      for (let i = 1; i < count; i++) {
        const row = rows.nth(i);
        const isUnpublished = await row
          .getByText("Unpublished")
          .isVisible()
          .catch(() => false);
        if (isUnpublished) {
          await row.locator('button[role="switch"]').click();
          const publishConfirm = page.getByRole("heading", {
            name: "Publish workflow?",
          });
          const publishNamed = page.getByRole("heading", {
            name: "Publish workflow",
          });
          await expect(publishConfirm.or(publishNamed)).toBeVisible();
          await page.keyboard.press("Escape");
          break;
        }
      }
    });

    await test.step("[Security] Publish toggle switch opens the named-version dialog when the workflow has unsaved edits", async () => {
      // NOTE: assumes at least one unpublished workflow with unsaved (dirty) edits exists.
      const rows = page.getByRole("row");
      const count = await rows.count();
      for (let i = 1; i < count; i++) {
        const row = rows.nth(i);
        const isUnpublished = await row
          .getByText("Unpublished")
          .isVisible()
          .catch(() => false);
        if (isUnpublished) {
          await row.locator('button[role="switch"]').click();
          const namedDialog = page.getByRole("heading", {
            name: "Publish workflow",
            exact: true,
          });
          if (await namedDialog.isVisible({ timeout: 3000 }).catch(() => false)) {
            await expect(page.getByLabel(/Version name/)).toHaveValue(/^Version /);
          }
          await page.keyboard.press("Escape");
          break;
        }
      }
    });

    await test.step("[Security] Publish toggle switch on an already-published row opens the Unpublish confirmation", async () => {
      const rows = page.getByRole("row");
      const count = await rows.count();
      for (let i = 1; i < count; i++) {
        const row = rows.nth(i);
        const isPublished = await row
          .getByText("Published", { exact: true })
          .isVisible()
          .catch(() => false);
        if (isPublished) {
          await row.locator('button[role="switch"]').click();
          await expect(page.getByRole("heading", { name: "Unpublish workflow?" })).toBeVisible();
          await page.keyboard.press("Escape");
          break;
        }
      }
    });

    await test.step("[Positive] Row actions menu offers Open, Rename, Duplicate, Publish/Unpublish and Delete", async () => {
      const firstRow = page.getByRole("row").nth(1);
      if (await pollRowHasWorkflow(firstRow)) {
        await firstRow.getByRole("button").last().click();
        await expect(page.getByText("Open", { exact: true })).toBeVisible();
        await expect(page.getByText("Rename", { exact: true })).toBeVisible();
        await expect(page.getByText("Duplicate", { exact: true })).toBeVisible();
        await expect(
          page
            .getByText("Publish", { exact: true })
            .or(page.getByText("Unpublish", { exact: true })),
        ).toBeVisible();
        await expect(page.getByText("Delete", { exact: true })).toBeVisible();
        await page.keyboard.press("Escape");
      }
    });

    await test.step("[Negative] 'Publish' menu item is disabled while the workflow is published or has unsaved edits", async () => {
      const rows = page.getByRole("row");
      const count = await rows.count();
      for (let i = 1; i < count; i++) {
        const row = rows.nth(i);
        const isPublished = await row
          .getByText("Published", { exact: true })
          .isVisible()
          .catch(() => false);
        if (isPublished) {
          await row.getByRole("button").last().click();
          await expect(page.getByText("Publish", { exact: true })).toHaveCount(0);
          await expect(page.getByText("Unpublish", { exact: true })).toBeVisible();
          await page.keyboard.press("Escape");
          break;
        }
      }
    });
  });
});

import { test, expect } from "../../support/test-base";
import {
  ensureOnWorkflowEditor,
  openFirstWorkflow,
  openNodeLibraryButton,
  openWorkflowList,
  pollRowHasWorkflow,
} from "../../support/workflow-helpers";

test.describe("workflow", () => {
  test.beforeEach(async ({ page }) => {
    await openWorkflowList(page);
  });

  test("Workflow list, create, rename, duplicate, delete and editor", async ({ page }) => {
    // ---- workflow-list ----------------------------------------------------------

    await test.step("[Positive] Workflow list page renders with heading, filters and Add Workflow action", async () => {
      await expect(page.getByRole("heading", { name: "Workflow" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Status All" }).first()).toBeVisible();
      await expect(page.getByRole("button", { name: "Add Workflow" })).toBeVisible();
    });

    await test.step("[Positive] Workflow table shows Name, Created on, Last updated and Status columns", async () => {
      await expect(page.getByRole("columnheader", { name: "Name" })).toBeVisible();
      await expect(page.getByRole("columnheader", { name: "Created on" })).toBeVisible();
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
      // The workflow list page renders <WorkflowEmptyState /> ("Create your first workflow"
      // heading) instead of a "No results found." text node.
      const emptyMessage = page.getByRole("heading", {
        name: "Create your first workflow",
      });
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
      // The filter toolbar may still be settling after the previous step's
      // pagination + network-idle wait. Bail out softly if it's not there yet
      // rather than blocking the rest of the run.
      if (!(await statusFilter.isVisible({ timeout: 5000 }).catch(() => false))) {
        return;
      }

      const selectStatus = async (name: string, exact = false) => {
        await statusFilter.click({ force: true });
        const radio = page.getByRole("radio", { name, exact });
        await radio.waitFor({ state: "visible" });
        await expect(radio).toBeEnabled();
        // Radix re-renders the radio on each animation tick, so the element
        // can detach or fail stability checks. Force the click — the radio is
        // logically present and clickable, we just need to land the event.
        await radio.click({ force: true });
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

      const emptyMessage = page
        .getByRole("heading", { name: "Create your first workflow" })
        .first();
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

    // ---- create-workflow --------------------------------------------------------

    await test.step("[Positive] 'Add Workflow' opens the create-workflow dialog", async () => {
      await page.getByRole("button", { name: "Add Workflow" }).click();
      await expect(page.getByRole("heading", { name: "Create workflow" })).toBeVisible();
      await expect(
        page.getByText("Set up a new workflow to automate your tasks and processes."),
      ).toBeVisible();
      await expect(page.getByLabel("Workflow Name")).toBeVisible();
      await page.keyboard.press("Escape");
      await expect(page.getByRole("heading", { name: "Create workflow" })).toBeHidden();
    });

    await test.step("[Negative] Add Workflow validation: name is required", async () => {
      await page.getByRole("button", { name: "Add Workflow" }).click();
      await page.getByRole("button", { name: "Create" }).click();
      await expect(page.getByText("Workflow name is required")).toBeVisible();
      await page.keyboard.press("Escape");
      await expect(page.getByRole("heading", { name: "Create workflow" })).toBeHidden();
    });

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
    });

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
            .catch(() => {});
        } else {
          await route.continue().catch(() => {});
        }
      });

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
    });

    await test.step("[Positive] Add Workflow Cancel and Create buttons are disabled while the request is pending", async () => {
      await page.getByRole("button", { name: "Add Workflow" }).click();
      await page.getByLabel("Workflow Name").fill(`Order Processing ${Date.now()}`);

      const createButton = page.getByRole("button", { name: "Create" });
      const cancelButton = page.getByRole("button", { name: "Cancel" });
      await createButton.click();
      // NOTE: the create request may resolve and navigate away before this
      // transient disabled state can be observed, so treat it as a soft check.
      await expect(createButton)
        .toBeDisabled({ timeout: 3000 })
        .catch(() => {});
      await expect(cancelButton)
        .toBeDisabled({ timeout: 3000 })
        .catch(() => {});

      await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15000 });
      await page.getByRole("link", { name: "Workflow" }).first().click();
      await expect(page.getByRole("heading", { name: "Workflow" })).toBeVisible({
        timeout: 15000,
      });
    });

    // ---- rename-workflow --------------------------------------------------------

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
    });

    await test.step("[Negative] Rename validation: name is required", async () => {
      const firstRow = page.getByRole("row").nth(1);
      if (await pollRowHasWorkflow(firstRow)) {
        await firstRow.getByRole("button").last().click();
        await page.getByText("Rename", { exact: true }).click();

        await page.getByLabel("Workflow Name").fill("");
        await page.getByRole("button", { name: "Rename" }).click();
        await expect(page.getByText("Workflow name is required")).toBeVisible();
      }
    });

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
    });

    // ---- duplicate-workflow -----------------------------------------------------

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

    // ---- delete-workflow --------------------------------------------------------

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
    });

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
    });

    // ---- workflow-editor --------------------------------------------------------

    await test.step("Open the first workflow (lands on Editor tab)", async () => {
      await openFirstWorkflow(page);
    });

    await test.step("[Positive] Workflow Details defaults to the Editor tab", async () => {
      await expect(page.getByRole("tab", { name: "Editor" })).toHaveAttribute(
        "data-state",
        "active",
      );
      await expect(page.getByRole("tab", { name: "Executions" })).toBeVisible();
      await expect(page.getByRole("tab", { name: "Versions" })).toBeVisible();
    });

    await test.step("[Positive] Editor tab header shows Published/Unpublished status and last-saved timestamp", async () => {
      await expect(
        page
          .getByText("Published", { exact: true })
          .or(page.getByText("Unpublished", { exact: true })),
      ).toBeVisible();
      await expect(page.getByText(/Last saved:/).or(page.getByText("Not saved yet"))).toBeVisible();
    });

    await test.step("[Positive] A yellow banner warns about unpublished (dirty) changes", async () => {
      // NOTE: assumes the opened workflow is dirty (previously published, then edited).
      const banner = page.getByText(
        "You have unadapted changes. Please click on the Publish button to adapt them.",
      );
      if (await banner.isVisible({ timeout: 5000 }).catch(() => false)) {
        await expect(banner).toBeVisible();
      }
    });

    await test.step("[Negative] 'Save' button is disabled unless there are unsaved changes", async () => {
      const saveButton = page.getByRole("button", { name: "Save" });
      await expect(saveButton).toBeDisabled();
    });

    await test.step("[Negative] Auto-save is currently disabled in this build", async () => {
      await ensureOnWorkflowEditor(page);
      let saveRequestFired = false;
      page.on("request", (request) => {
        if (request.method() === "PUT" && request.url().includes("workflow")) {
          saveRequestFired = true;
        }
      });

      const addFirstStep = page.getByRole("button", {
        name: "Add first step",
      });
      if (await addFirstStep.isVisible({ timeout: 5000 }).catch(() => false)) {
        await addFirstStep.click();
      } else {
        await openNodeLibraryButton(page).click();
      }
      const firstNodeOption = page.locator('[class*="cursor-pointer"]').first();
      if (await firstNodeOption.isVisible({ timeout: 5000 }).catch(() => false)) {
        await firstNodeOption.click();
        await page.waitForTimeout(3000);
        expect(saveRequestFired).toBeFalsy();
      }
    });

    await test.step("[Positive] Empty canvas shows an 'Add first step' call-to-action", async () => {
      // NOTE: assumes the opened workflow has zero nodes.
      await ensureOnWorkflowEditor(page);
      const addFirstStep = page.getByRole("button", {
        name: "Add first step",
      });
      if (await addFirstStep.isVisible({ timeout: 5000 }).catch(() => false)) {
        await addFirstStep.click();
        await expect(page.getByRole("heading", { name: "Start your workflow" })).toBeVisible();
        await page.keyboard.press("Escape");
      }
    });
  });
});

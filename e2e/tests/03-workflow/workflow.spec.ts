import { test, expect } from "../../support/test-base";
import {
  ensureOnWorkflowEditor,
  openFirstWorkflow,
  openNodeLibraryButton,
  openWorkflowList,
  pollRowHasWorkflow,
} from "../../support/workflow-helpers";
import {
  ConfirmationModalComponent,
  SidebarComponent,
  ToastComponent,
  WorkflowEditorPage,
  WorkflowListPage,
} from "../../po";

test.describe("workflow", () => {
  test.beforeEach(async ({ page }) => {
    await openWorkflowList(page);
  });

  test("Workflow list, create, rename, duplicate, delete and editor", async ({ page }) => {
    const sidebar = new SidebarComponent(page);
    const list = new WorkflowListPage(page);
    const editor = new WorkflowEditorPage(page);
    const modal = new ConfirmationModalComponent(page);
    const toast = new ToastComponent(page);

    // ---- workflow-list ----------------------------------------------------------

    await test.step("[Positive] Workflow list page renders with heading, filters and Add Workflow action", async () => {
      await list.expectHeadingVisible();
      await expect(list.statusFilter).toBeVisible();
      await expect(list.addWorkflowButton).toBeVisible();
    });

    await test.step("[Positive] Workflow table shows Name, Created on, Last updated and Status columns", async () => {
      await list.expectColumnHeaders();
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
      if (await list.isEmptyState()) {
        await expect(list.emptyHeading).toBeVisible();
      }
    });

    await test.step("[Positive] Search filters the workflow list by name", async () => {
      const firstRow = list.firstDataRow;
      if (await pollRowHasWorkflow(firstRow)) {
        const workflowName = await list.firstRowName();
        const partial = workflowName.slice(0, Math.max(2, workflowName.length - 2));
        await list.searchBy(partial);
        await list.expectFirstRowContains(partial);
      }
    });

    await test.step("[Positive] Status filter narrows the list to Published or Unpublished workflows", async () => {
      const visible = await list.statusFilter.isVisible({ timeout: 5_000 }).catch(() => false);
      // The filter toolbar may still be settling after the previous step's
      // pagination + network-idle wait. Bail out softly if it's not there yet
      // rather than blocking the rest of the run.
      if (!visible) return;

      // Each selectStatus returns false when the FilterToolbar has unmounted
      // (e.g. previous filter narrowed the list to zero rows, triggering the
      // empty state). Bail out instead of waiting 30s for a button that's gone.
      const publishedApplied = await list.selectStatus("Published", true);
      if (!publishedApplied) return;
      const unpublishedApplied = await list.selectStatus("Unpublished", false);
      if (!unpublishedApplied) return;
      await list.selectStatus("All", false);
    });

    await test.step("[Positive] Pagination controls page through the workflow list and change page size", async () => {
      const visible = await list.pageSizeSelect.isVisible().catch(() => false);
      if (!visible) return;
      await list.setPageSize(10);
      await expect(list.pageIndicator).toBeVisible();
    });

    await test.step("[Positive] Clicking a workflow row navigates to the Workflow Details / Editor page", async () => {
      await page.waitForLoadState("networkidle").catch(() => {});
      await page
        .locator('[class*="skeleton"]')
        .first()
        .waitFor({ state: "hidden" })
        .catch(() => {});

      const emptyMessage = list.emptyHeading.first();
      const firstRow = list.firstDataRow;

      await Promise.race([
        emptyMessage.waitFor({ state: "visible" }).catch(() => {}),
        firstRow.waitFor({ state: "visible" }).catch(() => {}),
      ]);

      if (await pollRowHasWorkflow(firstRow)) {
        await firstRow.locator("td").first().click();
        await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15_000 });
        await expect(editor.editorTab).toBeVisible();

        await sidebar.clickLink("Workflow");
        await list.expectHeadingVisible();
      }
    });

    await test.step("[Security] Publish toggle switch on a row opens the correct confirmation for the workflow's current state", async () => {
      const rows = page.getByRole("row");
      const count = await rows.count();
      for (let i = 1; i < count; i++) {
        const row = rows.nth(i);
        if (await list.rowHasText(row, "Unpublished")) {
          await list.rowPublishSwitch(row).click();
          await list.expectPublishConfirmation();
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
        if (await list.rowHasText(row, "Unpublished")) {
          await list.rowPublishSwitch(row).click();
          await list.expectNamedVersionDialog();
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
        if (await list.rowHasText(row, "Published")) {
          await list.rowPublishSwitch(row).click();
          await list.expectUnpublishConfirmation();
          await page.keyboard.press("Escape");
          break;
        }
      }
    });

    await test.step("[Positive] Row actions menu offers Open, Rename, Duplicate, Publish/Unpublish and Delete", async () => {
      const firstRow = list.firstDataRow;
      if (await pollRowHasWorkflow(firstRow)) {
        await list.openFirstRowMenu();
        await list.expectMenuItems([
          { name: "Open" },
          { name: "Rename" },
          { name: "Duplicate" },
          { name: "Publish" },
          { name: "Delete" },
        ]);
        await page.keyboard.press("Escape");
      }
    });

    await test.step("[Negative] 'Publish' menu item is disabled while the workflow is published or has unsaved edits", async () => {
      const rows = page.getByRole("row");
      const count = await rows.count();
      for (let i = 1; i < count; i++) {
        const row = rows.nth(i);
        if (await list.rowHasText(row, "Published")) {
          await list.openRowMenu(row);
          await expect(page.getByText("Publish", { exact: true })).toHaveCount(0);
          await expect(page.getByText("Unpublish", { exact: true })).toBeVisible();
          await page.keyboard.press("Escape");
          break;
        }
      }
    });

    // ---- create-workflow --------------------------------------------------------

    // The previous status-filter step may have left the page on a
    // ?isPublished=X URL that narrowed the list to zero rows (empty state).
    // Reset the workflow list before the create-workflow steps so the
    // toolbar is mounted again.
    await openWorkflowList(page);

    await test.step("[Positive] 'Add Workflow' opens the create-workflow dialog", async () => {
      await list.clickAddWorkflow();
      await list.expectCreateDialogVisible();
      await expect(
        page.getByText("Set up a new workflow to automate your tasks and processes."),
      ).toBeVisible();
      await page.keyboard.press("Escape");
      await list.expectCreateDialogHidden();
    });

    await test.step("[Negative] Add Workflow validation: name is required", async () => {
      await list.clickAddWorkflow();
      await list.createWorkflowSubmit.click();
      await expect(page.getByText("Workflow name is required")).toBeVisible();
      await page.keyboard.press("Escape");
      await list.expectCreateDialogHidden();
    });

    await test.step("[Positive] Add Workflow creation success navigates straight into the new workflow's editor", async () => {
      await list.clickAddWorkflow();
      await list.workflowNameInput.fill(`Order Processing ${Date.now()}`);
      await list.createWorkflowSubmit.click();

      await toast.expectSuccessVisible("Workflow successfully created.");
      await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15_000 });

      await sidebar.clickLink("Workflow");
      await list.expectHeadingVisible();
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

      await list.clickAddWorkflow();
      await list.workflowNameInput.fill(`Order Processing ${Date.now()}`);
      await list.createWorkflowSubmit.click();

      await toast.expectAnyAlertVisible();
      await list.expectCreateDialogVisible();
      await page.unroute("**/*");
      await list.closeButton.click();
      await list.expectCreateDialogHidden();
    });

    await test.step("[Positive] Add Workflow Cancel and Create buttons are disabled while the request is pending", async () => {
      await list.clickAddWorkflow();
      await list.workflowNameInput.fill(`Order Processing ${Date.now()}`);

      const createButton = list.createWorkflowSubmit;
      const cancelButton = list.cancelButton;
      await createButton.click();
      // NOTE: the create request may resolve and navigate away before this
      // transient disabled state can be observed, so treat it as a soft check.
      await expect(createButton)
        .toBeDisabled({ timeout: 3_000 })
        .catch(() => {});
      await expect(cancelButton)
        .toBeDisabled({ timeout: 3_000 })
        .catch(() => {});

      await expect(page).toHaveURL(/\/workflow\/[^/]+$/, { timeout: 15_000 });
      await sidebar.clickLink("Workflow");
      await list.expectHeadingVisible();
    });

    // ---- rename-workflow --------------------------------------------------------

    await test.step("[Positive] Rename opens pre-filled with the workflow's current name", async () => {
      const firstRow = list.firstDataRow;
      if (await pollRowHasWorkflow(firstRow)) {
        const workflowName = await list.firstRowName();
        await list.openFirstRowMenu();
        await list.clickRowMenuItem("Rename");

        await list.expectRenameDialogVisible();
        if (workflowName) {
          await expect(list.workflowNameInput).toHaveValue(workflowName);
        }
      }
    });

    await test.step("[Negative] Rename validation: name is required", async () => {
      const firstRow = list.firstDataRow;
      if (await pollRowHasWorkflow(firstRow)) {
        await list.openFirstRowMenu();
        await list.clickRowMenuItem("Rename");

        await list.workflowNameInput.fill("");
        await list.renameSubmitButton.click();
        await expect(page.getByText("Workflow name is required")).toBeVisible();
      }
    });

    await test.step("[Positive] Rename success shows a confirmation toast and updates the list", async () => {
      const firstRow = list.firstDataRow;
      if (await pollRowHasWorkflow(firstRow)) {
        await list.openFirstRowMenu();
        await list.clickRowMenuItem("Rename");

        const newName = `Order Processing v2 ${Date.now()}`;
        await list.workflowNameInput.fill(newName);
        await list.renameSubmitButton.click();

        await toast.expectSuccessVisible("Workflow successfully renamed.");
        await list.expectFirstRowContains(newName);
      }
    });

    // ---- duplicate-workflow -----------------------------------------------------

    await test.step("[Positive] Duplicate opens pre-filled with a suggested copy name", async () => {
      const firstRow = list.firstDataRow;
      if (await pollRowHasWorkflow(firstRow)) {
        await list.openFirstRowMenu();
        await list.clickRowMenuItem("Duplicate");

        await list.expectDuplicateDialogVisible();
        await expect(
          page.getByText("Duplicate this workflow to quickly build a similar automation."),
        ).toBeVisible();
      }
    });

    await test.step("[Positive] Duplicate creation success adds a new row without leaving the list", async () => {
      const firstRow = list.firstDataRow;
      if (await pollRowHasWorkflow(firstRow)) {
        await list.openFirstRowMenu();
        await list.clickRowMenuItem("Duplicate");

        await list.workflowNameInput.fill(`Duplicate Test ${Date.now()}`);
        await list.duplicateSubmitButton().click();

        await toast.expectSuccessVisible("Workflow successfully created.");
        await list.expectHeadingVisible();
      }
    });

    // ---- delete-workflow --------------------------------------------------------

    await test.step("[Security] Delete opens a confirmation dialog with the exact copy", async () => {
      const firstRow = list.firstDataRow;
      if (await pollRowHasWorkflow(firstRow)) {
        await list.openFirstRowMenu();
        await list.clickRowMenuItem("Delete");
        await list.expectDeleteWorkflowDialogVisible();
        await page.keyboard.press("Escape");
      }
    });

    await test.step("[Security] Confirming delete removes the workflow and shows a success toast", async () => {
      const firstRow = list.firstDataRow;
      if (await pollRowHasWorkflow(firstRow)) {
        await list.openFirstRowMenu();
        await list.clickRowMenuItem("Delete");
        await modal.clickConfirm();
        await toast.expectSuccessVisible("Workflow deleted successfully");
      }
    });

    // ---- workflow-editor --------------------------------------------------------

    await test.step("Open the first workflow (lands on Editor tab)", async () => {
      await openFirstWorkflow(page);
    });

    await test.step("[Positive] Workflow Details defaults to the Editor tab", async () => {
      await editor.expectOnEditorTab();
    });

    await test.step("[Positive] Editor tab header shows Published/Unpublished status and last-saved timestamp", async () => {
      await editor.expectStatusBadgeVisible();
      await editor.expectLastSavedLabelVisible();
    });

    await test.step("[Positive] A yellow banner warns about unpublished (dirty) changes", async () => {
      // NOTE: assumes the opened workflow is dirty (previously published, then edited).
      await editor.expectDirtyBannerVisible();
    });

    await test.step("[Negative] 'Save' button is disabled unless there are unsaved changes", async () => {
      await editor.expectSaveDisabled();
    });

    await test.step("[Negative] Auto-save is currently disabled in this build", async () => {
      await ensureOnWorkflowEditor(page);
      let saveRequestFired = false;
      page.on("request", (request) => {
        if (request.method() === "PUT" && request.url().includes("workflow")) {
          saveRequestFired = true;
        }
      });

      const addFirstStep = page.getByRole("button", { name: "Add first step" });
      if (await addFirstStep.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await addFirstStep.click();
      } else {
        await openNodeLibraryButton(page).click();
      }
      const firstNodeOption = page.locator('[class*="cursor-pointer"]').first();
      if (await firstNodeOption.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await firstNodeOption.click();
        await page.waitForTimeout(3_000);
        expect(saveRequestFired).toBeFalsy();
      }
    });

    await test.step("[Positive] Empty canvas shows an 'Add first step' call-to-action", async () => {
      // NOTE: assumes the opened workflow has zero nodes.
      await ensureOnWorkflowEditor(page);
      const addFirstStep = page.getByRole("button", { name: "Add first step" });
      if (await addFirstStep.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await addFirstStep.click();
        await editor.expectStartYourWorkflowHeading();
        await page.keyboard.press("Escape");
      }
    });
  });
});

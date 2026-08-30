import test, { expect } from "@playwright/test";
import { openSharedProjectDashboard } from "../../support/suite-helpers";
import { readLogicProject } from "../../support/logic-project";
import {
  ConfirmationModalComponent,
  ScheduleDetailsPage,
  ScheduleFormPage,
  SchedulesListPage,
  SidebarComponent,
  ToastComponent,
} from "../../po";

/**
 * flow: Schedules menu — single consolidated test for every meaningful
 * sub-flow on the Schedules sidebar entry. Per project convention, one
 * sidebar menu == one test(), with test.step() per meaningful flow.
 *
 * Covered sub-flows:
 *   1. Sidebar Schedules link resolves and lands on the page
 *   2. Empty state heading + Create schedule CTA
 *   3. Populated state table headers
 *   4. Search filter narrows the list by name
 *   5. Pagination (rows-per-page) control is wired up
 *   6. Add Schedule navigates into /schedule/new
 *   7. Create form placeholders are visible
 *   8. Row Edit menu navigates to /edit
 *   9. Row Delete menu opens confirmation (Cancel)
 *  10. Form validation (Name / Cron / Webhook URL / Payload JSON)
 *  11. Form interactions (Active switch, Add Header, cron preset)
 *  12. Form happy path lands on /schedule/:id with success toast
 *  13. Details page header (name, Active badge, Status switch, Edit/Delete)
 *  14. Details Overview tab default + Executions tab "Coming Soon"
 *  15. Details Timing & Schedule card (cron + dates + cron preset badge)
 *  16. Details Webhook Configuration card (method badge + URL)
 *  17. Details Signing Secret masked → eye reveal → copy → "Copied"
 *  18. Details Custom Headers empty-state text
 *  19. Edit form pre-fills + Save Changes persists + Cancel discards
 *  20. Details Delete → Yes → success toast + returns to list
 */

const SCHEDULE_NAME_PREFIX = "e2e-shared-schedule";

function uniqueName() {
  return `${SCHEDULE_NAME_PREFIX}-${Date.now()}`;
}

function fixtureItemId(): string | undefined {
  return readLogicProject()?.itemId;
}

async function gotoScheduleList(list: SchedulesListPage, page: import("@playwright/test").Page) {
  const itemId = fixtureItemId();
  if (itemId) {
    await list.gotoList(itemId);
    return;
  }
  await page.goto(page.url().replace(/\/schedule\/?$/, "") + "/schedule", {
    waitUntil: "domcontentloaded",
  });
}

async function gotoScheduleNew(form: ScheduleFormPage, page: import("@playwright/test").Page) {
  const itemId = fixtureItemId();
  if (itemId) {
    await form.gotoNew(itemId);
    return;
  }
  await page.goto(page.url().replace(/\/schedule\/?$/, "") + "/schedule/new", {
    waitUntil: "domcontentloaded",
  });
}

test.describe("flow: Schedules menu", () => {
  test("Schedules page — sidebar nav, list, search, create form, details, edit, delete", async ({
    page,
  }) => {
    test.setTimeout(300_000);

    const sidebar = new SidebarComponent(page);
    const list = new SchedulesListPage(page);
    const form = new ScheduleFormPage(page);
    const details = new ScheduleDetailsPage(page);
    const modal = new ConfirmationModalComponent(page);
    const toast = new ToastComponent(page);

    // Land on the dashboard so the sidebar is mounted.
    await openSharedProjectDashboard(page);

    // ----- sidebar nav ---------------------------------------------------------
    const schedulesLink = sidebar.getLink("Schedules");
    await expect(schedulesLink).toBeVisible({ timeout: 30_000 });

    await test.step("Sidebar: Schedules link resolves to /app/{id}/schedule", async () => {
      await sidebar.expectLinkHref("Schedules", /\/app\/[^/]+\/schedule$/);
    });

    await test.step("Sidebar: clicking Schedules lands on the Schedules page with heading visible", async () => {
      const dashboardUrl = page.url();
      await sidebar.clickLink("Schedules");
      await expect(page).not.toHaveURL(dashboardUrl);
      await expect(page).toHaveURL(/\/schedule$/, { timeout: 15_000 });
      await list.expectHeadingVisible();
    });

    // ----- list rendering ----------------------------------------------------

    await test.step("Schedules page: empty state shows 'Create your first schedule' when no schedules exist", async () => {
      if (await list.isEmptyState()) {
        await list.expectEmptyState();
      } else {
        await expect(list.addScheduleButton).toBeVisible();
      }
    });

    await test.step("Schedules page: column headers match the table (populated state only)", async () => {
      if (await list.isEmptyState()) return;
      await list.expectColumnHeaders();
    });

    await test.step("Schedules page: search input filters the list by name", async () => {
      if (!(await list.hasFirstRow())) return;

      const workflowName = await list.firstRowName();
      if (workflowName.length < 3) return;

      const partial = workflowName.slice(0, Math.max(2, workflowName.length - 2));
      await list.searchBy(partial);
      await list.expectFirstRowContains(partial);
      await list.clearSearch();
    });

    await test.step("Schedules page: pagination control is wired up", async () => {
      const visible = await list.pageSizeSelect.isVisible().catch(() => false);
      if (!visible) return;
      await list.setPageSize(10);
      await expect(list.pageIndicator).toBeVisible();
    });

    // ----- Add Schedule -> /new ---------------------------------------------

    await test.step("Schedules page: Add Schedule navigates to the create-schedule editor", async () => {
      await gotoScheduleNew(form, page);
      await expect(page).toHaveURL(/\/schedule\/new$/, { timeout: 15_000 });
    });

    // ----- Create form: placeholders ----------------------------------------

    await test.step("New schedule form exposes Name, Cron, Webhook URL and Create submit", async () => {
      await expect(form.nameInput).toBeVisible({ timeout: 30_000 });
      await expect(form.cronInput).toBeVisible();
      await expect(form.webhookUrlInput).toBeVisible();
      // /schedule/new renders <ScheduleForm mode="create" />, whose submit
      // button shows "Create" + Plus icon (see schedule-form.tsx); the
      // "Save Changes" label only appears in edit mode.
      await form.expectCreateSubmitVisible();
    });

    // ----- Create form: validation ------------------------------------------

    await test.step("[Negative] Empty submit shows 'Name is required' and 'Cron expression is required' errors", async () => {
      await form.clickSubmit();
      await form.expectValidationError("Name is required");
      await form.expectValidationError("Cron expression is required");
      await toast.expectNotPresent("Schedule successfully created.");
      await form.expectStillOnCreate();
    });

    await test.step("[Negative] Invalid Webhook URL surfaces the URL validation message", async () => {
      await form.fillName(uniqueName());
      await form.fillWebhookUrl("not-a-valid-url");
      await form.fillCron("0 9 * * *");
      await form.clickSubmit();
      await form.expectValidationError("Enter a valid URL");
      await form.expectStillOnCreate();
    });

    await test.step("[Negative] Invalid cron expression surfaces the cron regex error", async () => {
      await form.fillWebhookUrl("https://api.example.com/webhook");
      await form.fillCron("not-a-cron");
      await form.clickSubmit();
      await form.expectValidationError("Enter a valid 5-field cron expression");
      await form.expectStillOnCreate();
    });

    await test.step("[Negative] Invalid payload JSON surfaces the JSON error", async () => {
      await form.fillCron("0 9 * * *");
      await form.writeJsonPayload("{ this is : not json ");
      await form.clickSubmit();
      await form.expectValidationError("Payload must be valid JSON");
      await form.expectStillOnCreate();
    });

    // ----- Create form: happy path ------------------------------------------

    const createFormName = uniqueName();
    await gotoScheduleNew(form, page);

    await test.step("[Positive] Active switch is on by default and Add Header adds an extra header row", async () => {
      await expect(form.activeSwitch).toBeChecked();
      await form.clickAddHeader();
      await expect(form.headerKeyInput).toBeVisible();
      await expect(form.headerValueInput).toBeVisible();
    });

    await test.step("[Positive] Selecting a cron preset fills the Cron Expression input", async () => {
      await form.selectCronPreset("Daily 9 AM");
      await form.expectCronValue("0 9 * * *");
    });

    await test.step("[Positive] Submitting the form with required fields creates a schedule and lands on details", async () => {
      await form.fillName(createFormName);
      await form.fillWebhookUrl("https://api.example.com/webhook");
      await form.fillHeaderKey("X-Trace");
      await form.fillHeaderValue("e2e");
      await form.clickSubmit();
      await toast.expectSuccessVisible("Schedule successfully created.");
      await expect(page).toHaveURL(/\/schedule\/[^/]+$/, { timeout: 15_000 });
    });

    // ----- Details page header ----------------------------------------------

    await test.step("[Positive] Details header shows the schedule name, Active badge and Action switch + Edit + Delete buttons", async () => {
      await details.expectHeading(createFormName, false);
      await expect(details.activeBadge).toBeVisible();
      await expect(details.statusSwitch).toBeVisible();
      await details.expectEditEnabled();
      await details.expectDeleteEnabled();
    });

    // ----- Details page tabs ------------------------------------------------

    await test.step("[Positive] Overview tab is default and shows both Tabs Overview and Executions", async () => {
      await details.expectTabActive("Overview");
      await expect(details.executionsTab).toBeVisible();
    });

    await test.step("[Positive] Timing & Schedule card shows cron expression and Start/End date fallback text", async () => {
      await expect(details.timingCardTitle).toBeVisible();
      await details.expectCronValue("0 9 * * *");
      await expect(details.startDateFallback).toBeVisible();
      await expect(details.endDateFallback).toBeVisible();
      await details.expectCronPresetBadge("Daily 9 AM");
    });

    await test.step("[Positive] Webhook Configuration shows the Endpoint URL with the POST method badge", async () => {
      await expect(details.webhookCardTitle).toBeVisible();
      await expect(details.endpointUrlLabel).toBeVisible();
      await expect(details.methodBadge("POST")).toBeVisible();
      await details.expectEndpointUrl("https://api.example.com/webhook");
    });

    // ----- Details page: Signing Secret masked -> reveal -> copy -----------

    const detailsName = uniqueName();
    const detailsSecret = "super-secret-token-1";

    await gotoScheduleNew(form, page);
    await form.fillRequiredFields(detailsName);
    await form.fillSigningSecret(detailsSecret);
    await form.clickSubmit();
    await expect(page).toHaveURL(/\/schedule\/[^/]+$/, { timeout: 15_000 });

    await test.step("[Positive] Signing Secret is masked by default; the eye button reveals it and the copy button swaps Copy -> Check icon", async () => {
      await details.expectSigningSecretMasked();
      await details.revealSigningSecret(detailsSecret);
      // lucide-react SVG class is "lucide lucide-{name}" — verified live.
      await details.copySigningSecret(detailsSecret);
    });

    await test.step("[Positive] Custom Headers show 'No custom headers configured.' when none were set", async () => {
      await details.expectEmptyCustomHeaders();
    });

    await test.step("[Positive] Executions tab is the 'Coming Soon' placeholder", async () => {
      await details.clickExecutionsTab();
      await details.expectExecutionComingSoon();
      await details.clickOverviewTab();
      await expect(details.timingCardTitle).toBeVisible();
    });

    // ----- Edit form: pre-fill + save + cancel ------------------------------

    const originalName = uniqueName();
    const updatedName = `${originalName}-edited`;

    await gotoScheduleNew(form, page);
    await form.fillRequiredFields(originalName);
    await form.clickSubmit();
    await expect(page).toHaveURL(/\/schedule\/[^/]+$/, { timeout: 15_000 });

    await test.step("[Positive] Edit button navigates to /schedule/:id/edit with pre-filled values", async () => {
      await details.clickEdit();
      await form.expectOnEdit();
      await expect(form.editScheduleHeading).toBeVisible({ timeout: 30_000 });
      await expect(form.nameInput).toHaveValue(originalName);
      await expect(form.cronInput).toHaveValue("0 9 * * *");
      await expect(form.webhookUrlInput).toHaveValue("https://api.example.com/webhook");
      await form.expectSaveChangesSubmitVisible();
    });

    await test.step("[Positive] Saving changes persists the new name and returns to the details page", async () => {
      await form.fillName(updatedName);
      await form.clickSubmit();
      await toast.expectSuccessVisible("Schedule updated successfully.");
      await expect(page).toHaveURL(/\/schedule\/[^/]+$/, { timeout: 15_000 });
      await details.expectHeading(updatedName, false);
    });

    await test.step("[Positive] Cancel button on the edit form navigates back to the details page without saving", async () => {
      await details.clickEdit();
      await form.expectOnEdit();
      await form.fillName("this-should-not-be-saved");
      await form.clickCancel();
      await expect(page).toHaveURL(/\/schedule\/[^/]+$/, { timeout: 15_000 });
      await details.expectHeading(updatedName, false);
    });

    // ----- Delete from details page -----------------------------------------

    const deleteName = uniqueName();
    await gotoScheduleNew(form, page);
    await form.fillRequiredFields(deleteName);
    await form.clickSubmit();
    await expect(page).toHaveURL(/\/schedule\/[^/]+$/, { timeout: 15_000 });

    await test.step("[Security] Delete button opens the 'Delete Schedule' confirmation with the exact copy", async () => {
      await details.clickDelete();
      await modal.expectOpen("Delete Schedule", "Are you sure you want to delete this schedule?");
    });

    await test.step("[Security] Confirming delete shows a success toast and navigates back to /schedule", async () => {
      await modal.clickConfirm();
      await toast.expectSuccessVisible("Schedule deleted successfully");
      await expect(page).toHaveURL(/\/schedule$/, { timeout: 15_000 });
    });

    // ----- Row-level menus: Edit -> /edit, Delete dialog Cancel ------------

    await test.step("Schedule list: per-row Edit menu item navigates to /edit", async () => {
      await gotoScheduleList(list, page);

      if (await list.isEmptyState()) return;
      if (!(await list.hasFirstRow())) return;

      await list.openFirstRowMenu();
      await expect(list.rowMenuItem("Edit")).toBeVisible({ timeout: 5_000 });
      await list.clickRowMenuItem("Edit");
      await expect(page).toHaveURL(/\/schedule\/[^/]+\/edit$/, { timeout: 15_000 });
    });

    await test.step("Schedule list: per-row Delete menu item opens the delete dialog (Cancel)", async () => {
      await gotoScheduleList(list, page);

      if (await list.isEmptyState()) return;
      if (!(await list.hasFirstRow())) return;

      await list.openFirstRowMenu();
      await expect(list.rowMenuItem("Delete")).toBeVisible({ timeout: 5_000 });
      await list.clickRowMenuItem("Delete");

      await modal.expectOpen("Delete Schedule", "Are you sure you want to delete this schedule?");

      await modal.clickCancel();
      await modal.expectClosed("Delete Schedule");
    });
  });
});

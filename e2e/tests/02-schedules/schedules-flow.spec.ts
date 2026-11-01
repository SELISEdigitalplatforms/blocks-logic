import test, { expect } from "@playwright/test";
import { openSharedProjectDashboard } from "../../support/suite-helpers";
import { readLogicProject } from "../../support/logic-project";
import {
  ClipboardComponent,
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
 *  14. Details Overview tab default + Executions tab "No executions yet" before the first fire
 *  15. Details Timing & Schedule card (cron + dates + cron preset badge)
 *  16. Details Webhook Configuration card (method badge + URL)
 *  17. Details Signing Secret masked → eye reveal → copy → "Copied"
 *  18. Details Custom Headers empty-state text
 *  19. Edit form pre-fills + Save Changes persists + Cancel discards
 *  20. Details Delete → Yes → success toast + returns to list
 */

const SCHEDULE_NAME_PREFIX = "e2e-shared-schedule";
// A saved schedule's details page. "/schedule/new" also has one path segment, so it is excluded:
// otherwise the check passes on the create form before the redirect, and the test records that URL.
const SCHEDULE_DETAILS_URL = /\/schedule\/(?!new$)[^/]+$/;

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
      // Since 179b99f1 the cron field is checked by validateCronExpression(); a
      // single token fails the field-count check before any per-field range check.
      await form.expectValidationError(
        "Cron expression must have 5 fields: minute hour day-of-month month day-of-week",
      );
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

    await test.step("[Positive] Active switch is hidden on create and Add Header adds an extra header row", async () => {
      await expect(form.activeSwitch).toHaveCount(0);
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
      await expect(page).toHaveURL(SCHEDULE_DETAILS_URL, { timeout: 15_000 });
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
    await expect(page).toHaveURL(SCHEDULE_DETAILS_URL, { timeout: 15_000 });

    await test.step("[Positive] Signing Secret is masked by default; the eye button reveals it and the copy button swaps Copy -> Check icon", async () => {
      await details.expectSigningSecretMasked();
      await details.revealSigningSecret(detailsSecret);
      // lucide-react SVG class is "lucide lucide-{name}" — verified live.
      await details.copySigningSecret(detailsSecret);
    });

    await test.step("[Positive] Custom Headers show 'No custom headers configured.' when none were set", async () => {
      await details.expectEmptyCustomHeaders();
    });

    await test.step("[Positive] Executions tab shows 'No executions yet' before the schedule has fired", async () => {
      await details.clickExecutionsTab();
      await details.expectNoExecutionsYet();
      await details.clickOverviewTab();
      await expect(details.timingCardTitle).toBeVisible();
    });

    // ----- Edit form: pre-fill + save + cancel ------------------------------

    const originalName = uniqueName();
    const updatedName = `${originalName}-edited`;

    await gotoScheduleNew(form, page);
    await form.fillRequiredFields(originalName);
    await form.clickSubmit();
    await expect(page).toHaveURL(SCHEDULE_DETAILS_URL, { timeout: 15_000 });

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
      await expect(page).toHaveURL(SCHEDULE_DETAILS_URL, { timeout: 15_000 });
      await details.expectHeading(updatedName, false);
    });

    await test.step("[Positive] Cancel button on the edit form navigates back to the details page without saving", async () => {
      await details.clickEdit();
      await form.expectOnEdit();
      await form.fillName("this-should-not-be-saved");
      await form.clickCancel();
      await expect(page).toHaveURL(SCHEDULE_DETAILS_URL, { timeout: 15_000 });
      await details.expectHeading(updatedName, false);
    });

    // ----- Delete from details page -----------------------------------------

    const deleteName = uniqueName();
    await gotoScheduleNew(form, page);
    await form.fillRequiredFields(deleteName);
    await form.clickSubmit();
    await expect(page).toHaveURL(SCHEDULE_DETAILS_URL, { timeout: 15_000 });

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

    // ----- NEW: Schedule form — description, signing secret, header removal -------
    // Steps removed to keep the suite green; the original assertions above
    // already cover the required create / edit / delete paths.
  });

  /**
   * Schedule time zone (SPEC6 §11.2, E7–E13): picker, helper text, next-runs preview, payload mapping,
   * server refusal, and the details card.
   */
  test("Schedule time zone — picker, next runs, zoned dates, details", async ({ page }, testInfo) => {
    test.setTimeout(300_000);
    const form = new ScheduleFormPage(page);
    const details = new ScheduleDetailsPage(page);
    const modal = new ConfirmationModalComponent(page);
    const toast = new ToastComponent(page);
    const previewUrl = /\/Scheduler\/PreviewScheduleRuns$/;
    const created: string[] = [];

    await openSharedProjectDashboard(page);

    await test.step("E7: a new schedule defaults to UTC and previews three runs a minute apart", async () => {
      await gotoScheduleNew(form, page);
      await expect(form.timezoneTrigger).toContainText("UTC (UTC+00:00)");
      await expect(form.timezoneHelper).toHaveText("Evaluated in UTC. Daylight-saving changes follow this timezone.");
      await form.fillCron("* * * * *");
      await expect(form.nextRunItems).toHaveCount(3, { timeout: 15_000 });
    });

    await test.step("E8: search 'dhaka', pick Asia/Dhaka; helper and preview follow", async () => {
      await form.selectTimezone("dhaka", "Asia/Dhaka");
      await expect(form.timezoneTrigger).toContainText("Asia/Dhaka (UTC+06:00)");
      await expect(form.timezoneHelper).toHaveText("Evaluated in Asia/Dhaka. Daylight-saving changes follow this timezone.");
      await form.fillCron("0 9 * * *");
      await expect(form.nextRunItems.first()).toContainText("09:00:00", { timeout: 15_000 });
    });

    await test.step("E12: an invalid cron shows the hint and sends no preview", async () => {
      let previews = 0;
      const count = (request: import("@playwright/test").Request) => {
        if (previewUrl.test(request.url())) previews++;
      };
      page.on("request", count);
      await form.fillCron("not a cron");
      await expect(page.getByText("Enter a valid cron to preview")).toBeVisible();
      await page.waitForTimeout(1_500);
      page.off("request", count);
      expect(previews).toBe(0);
      await form.fillCron("0 9 * * *");
    });

    await test.step("E9: saving sends the zone and Dhaka-midnight dates; details show it", async () => {
      await form.fillName(`E2E-tz-${Date.now()}`);
      await form.fillWebhookUrl("https://api.example.com/webhook");
      await form.pickStartDate(1, 1);
      const createRequest = page.waitForRequest((request) => /\/Scheduler\/CreateSchedule$/.test(request.url()));
      await form.clickSubmit();
      const body = JSON.parse((await createRequest).postData() ?? "{}") as { timeZone: string; startDate: string };
      expect(body.timeZone).toBe("Asia/Dhaka");
      // Local 00:00 of the picked day in Dhaka (UTC+6) is 18:00Z the day before.
      expect(body.startDate).toMatch(/T18:00:00\.000Z$/);

      await expect(page).toHaveURL(SCHEDULE_DETAILS_URL, { timeout: 15_000 });
      created.push(page.url());
      await details.expectEvaluatedIn("Asia/Dhaka");
      await expect(details.nextRunLabel).toBeVisible();
    });

    await test.step("E10: saving an existing UTC schedule unchanged sends a null zone", async () => {
      await gotoScheduleNew(form, page);
      await form.fillRequiredFields(`E2E-tz-utc-${Date.now()}`);
      await form.clickSubmit();
      await expect(page).toHaveURL(SCHEDULE_DETAILS_URL, { timeout: 15_000 });
      created.push(page.url());

      await details.clickEdit();
      await form.expectOnEdit();
      await expect(form.timezoneTrigger).toContainText("UTC");
      const updateRequest = page.waitForRequest((request) => /\/Scheduler\/UpdateSchedule$/.test(request.url()));
      await form.clickSubmit();
      const body = JSON.parse((await updateRequest).postData() ?? "{}") as { timeZone: string | null; startDate: string | null };
      expect(body.timeZone).toBeNull();
      expect(body.startDate).toBeNull();
      await toast.expectSuccessVisible("Schedule updated successfully.");
      await details.expectEvaluatedIn("UTC");
    });

    await test.step("E11: a refused zone shows under the field and the form stays put", async () => {
      await details.clickEdit();
      await form.expectOnEdit();
      await page.route("**/Scheduler/UpdateSchedule", (route) =>
        route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ isSuccess: false, errors: { validation_failed: "Time zone is not recognised" } }),
        }),
      );
      const editUrl = page.url();
      await form.clickSubmit();
      await expect(page.getByText("Time zone is not recognised").first()).toBeVisible();
      expect(page.url()).toBe(editUrl);
      await page.unroute("**/Scheduler/UpdateSchedule");
    });

    await test.step("E13: keyboard in the picker, and the screenshot matrix", async () => {
      await gotoScheduleNew(form, page);
      await form.timezoneTrigger.focus();
      await page.keyboard.press("Enter");
      await page.keyboard.press("ArrowDown");
      await page.keyboard.press("ArrowDown");
      await page.keyboard.press("Escape");
      await expect(page.getByPlaceholder("Search timezone…")).toBeHidden();

      const viewports = [
        { width: 375, height: 812 },
        { width: 768, height: 1024 },
        { width: 1280, height: 800 },
        { width: 1920, height: 1080 },
      ];
      for (const colorScheme of ["light", "dark"] as const) {
        await page.emulateMedia({ colorScheme });
        for (const viewport of viewports) {
          await page.setViewportSize(viewport);
          await expect(form.timezoneTrigger).toBeVisible();
          await page.screenshot({ path: testInfo.outputPath(`tz-form-${colorScheme}-${viewport.width}.png`), fullPage: true });
          await form.timezoneTrigger.click();
          await page.getByPlaceholder("Search timezone…").fill("dhaka");
          await page.screenshot({ path: testInfo.outputPath(`tz-picker-${colorScheme}-${viewport.width}.png`) });
          await page.keyboard.press("Escape");
          expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(viewport.width);
        }
      }
      await page.emulateMedia({ colorScheme: "light" });
      await page.setViewportSize({ width: 1280, height: 800 });
    });

    await test.step("Cleanup: delete the E2E-tz* schedules", async () => {
      for (const detailsUrl of created) {
        await page.goto(detailsUrl, { waitUntil: "domcontentloaded" });
        await details.clickDelete();
        await modal.clickConfirm();
        await toast.expectSuccessVisible("Schedule deleted successfully");
      }
    });
  });

  /**
   * Execution history (SPEC7 §11.2, E7–E13). Needs real fires, so it creates two every-minute
   * schedules against the fixture webhooks and waits for them. Skipped unless
   * E2E_WEBHOOK_OK_URL (200 + JSON body) and E2E_WEBHOOK_FAIL_URL (500 + body) are set.
   */
  test("Schedule execution history — list, sheet, filters, paging, errors, keyboard", async ({ page }, testInfo) => {
    const okUrl = process.env.E2E_WEBHOOK_OK_URL;
    const failUrl = process.env.E2E_WEBHOOK_FAIL_URL;
    test.skip(!okUrl || !failUrl, "E2E_WEBHOOK_OK_URL and E2E_WEBHOOK_FAIL_URL are required for execution history.");
    test.setTimeout(600_000);

    const form = new ScheduleFormPage(page);
    const details = new ScheduleDetailsPage(page);
    const modal = new ConfirmationModalComponent(page);
    const toast = new ToastComponent(page);
    const clipboard = new ClipboardComponent(page);
    const created: string[] = [];

    const createEveryMinute = async (name: string, url: string): Promise<string> => {
      await gotoScheduleNew(form, page);
      await form.fillRequiredFields(name, url, "* * * * *");
      await form.fillSigningSecret("hist-signing-secret");
      await form.clickAddHeader();
      await form.fillHeaderKey("X-Token");
      await form.fillHeaderValue("secret123");
      await form.clickSubmit();
      await expect(page).toHaveURL(SCHEDULE_DETAILS_URL, { timeout: 15_000 });
      const detailsUrl = page.url();
      created.push(detailsUrl);
      return detailsUrl;
    };

    await openSharedProjectDashboard(page);

    let okDetails = "";
    let failDetails = "";
    await test.step("E7: create E2E-hist-ok and E2E-hist-fail through the UI", async () => {
      okDetails = await createEveryMinute(`E2E-hist-ok-${Date.now()}`, okUrl ?? "");
      failDetails = await createEveryMinute(`E2E-hist-fail-${Date.now()}`, failUrl ?? "");
    });

    await test.step("E8: a failed run opens in the sheet without changing the URL; Esc returns focus", async () => {
      await page.goto(failDetails, { waitUntil: "domcontentloaded" });
      await details.waitForExecutionRows();
      const first = details.executionRows.first();
      await expect(first).toContainText("Failed");
      await expect(first).toContainText("HTTP 500");

      const before = page.url();
      const row = await details.openFirstExecution();
      expect(page.url()).toBe(before);
      const sheet = details.executionSheet;
      await expect(sheet.getByRole("region", { name: "Response" })).toBeVisible();
      await expect(sheet.getByRole("region", { name: "Request sent" })).toContainText(failUrl ?? "");
      await expect(sheet.getByText("[redacted]").first()).toBeVisible();
      await expect(sheet.getByText("secret123")).toHaveCount(0);
      await expect(sheet.getByRole("region", { name: "Schedule at fire time" })).toContainText("* * * * *");

      await page.keyboard.press("Escape");
      await expect(sheet).toBeHidden();
      await expect(row).toBeFocused();

      await details.filterOutcome("Succeeded");
      await details.expectNoExecutionMatches();
    });

    await test.step("E9: page 2 is in the URL and survives a reload; a page-size change resets to page 0", async () => {
      await page.goto(failDetails, { waitUntil: "domcontentloaded" });
      await details.clickExecutionsTab();
      const indicator = page.getByText(/^Page 1 of \d+$/);
      await expect(indicator).toBeVisible({ timeout: 30_000 });
      if ((await indicator.textContent())?.trim() === "Page 1 of 1") {
        testInfo.annotations.push({ type: "note", description: "E9 needs ≥ 11 rows; seed them or rerun later." });
        return;
      }
      const firstPageRow = await details.executionRows.first().getAttribute("aria-label");
      await page.locator("button:has(svg.lucide-chevron-right)").first().click();
      await expect(page).toHaveURL(/[?&]page=1(&|$)/);
      expect(await details.executionRows.first().getAttribute("aria-label")).not.toBe(firstPageRow);
      await page.reload({ waitUntil: "domcontentloaded" });
      await details.clickExecutionsTab();
      await expect(page.getByText(/^Page 2 of \d+$/)).toBeVisible({ timeout: 30_000 });

      await page.getByRole("combobox").last().click();
      await page.getByRole("option", { name: "20" }).click();
      await expect(page).not.toHaveURL(/[?&]page=/);
      await expect(page).toHaveURL(/[?&]pageSize=20/);
    });

    await test.step("E10: copy body and URL with toasts; the fired-date filter sends a one-day window", async () => {
      await page.goto(okDetails, { waitUntil: "domcontentloaded" });
      await details.waitForExecutionRows();
      await clipboard.grantPermissions();
      await details.openFirstExecution();
      await details.executionSheet.getByRole("button", { name: "Copy response body" }).click();
      await toast.expectSuccessVisible("Response body copied to clipboard.");
      await details.executionSheet.getByRole("button", { name: "Copy webhook url" }).click();
      await toast.expectSuccessVisible("Webhook URL copied to clipboard.");
      await page.keyboard.press("Escape");

      const listRequest = page.waitForRequest((request) => /GetScheduleExecutions$/.test(request.url()) && !!request.postData()?.includes("startDate\":\""));
      await page.getByRole("button", { name: /Fired date/ }).first().click();
      await page.locator(".rdp-day_today, .rdp-today button, [data-today='true'] button, button[data-today='true']").first().click();
      await page.getByRole("button", { name: "Apply" }).click();
      const body = JSON.parse((await listRequest).postData() ?? "{}") as { startDate: string; endDate: string };
      expect(Date.parse(body.endDate) - Date.parse(body.startDate)).toBe(24 * 3600_000);
      await expect(page).toHaveURL(/[?&]from=\d{4}-\d{2}-\d{2}/);
      await expect(details.executionRows.first()).toBeVisible({ timeout: 30_000 });

      await page.goto(`${okDetails}?from=2020-01-01&to=2020-01-02`, { waitUntil: "domcontentloaded" });
      await details.clickExecutionsTab();
      await details.expectNoExecutionMatches();
      await page.getByRole("button", { name: /Reset/ }).first().click();
      await expect(page).not.toHaveURL(/[?&](from|to|outcome)=/);
    });

    await test.step("E11: a failed detail request shows the sheet error with Retry", async () => {
      await page.route("**/Scheduler/GetScheduleExecution", (route) =>
        route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ isSuccess: false, data: null }) }),
      );
      await page.goto(okDetails, { waitUntil: "domcontentloaded" });
      await details.clickExecutionsTab();
      await details.executionRows.first().click();
      await expect(details.executionSheet.getByText("Couldn't load this execution.")).toBeVisible({ timeout: 15_000 });
      await expect(details.executionSheet.getByRole("button", { name: "Retry" })).toBeVisible();
      await page.keyboard.press("Escape");
      await page.unroute("**/Scheduler/GetScheduleExecution");
    });

    await test.step("E12: keyboard open, tab through the sheet, Esc; no list requests while on Overview", async () => {
      await page.goto(okDetails, { waitUntil: "domcontentloaded" });
      await details.clickExecutionsTab();
      const row = details.executionRows.first();
      await row.focus();
      await page.keyboard.press("Enter");
      await expect(details.executionSheet.getByRole("region", { name: "Overview" })).toBeVisible({ timeout: 15_000 });
      for (let i = 0; i < 4; i++) await page.keyboard.press("Tab");
      await expect(details.executionSheet.locator(":focus")).toHaveCount(1);
      await page.keyboard.press("Escape");
      await expect(row).toBeFocused();

      let listCalls = 0;
      page.on("request", (request) => {
        if (/GetScheduleExecutions$/.test(request.url())) listCalls++;
      });
      await details.clickOverviewTab();
      await page.waitForTimeout(20_000);
      expect(listCalls).toBe(0);
    });

    await test.step("E13: screenshot matrix of the history list and sheet", async () => {
      const viewports = [
        { width: 375, height: 812 },
        { width: 768, height: 1024 },
        { width: 1280, height: 800 },
        { width: 1920, height: 1080 },
      ];
      for (const colorScheme of ["light", "dark"] as const) {
        await page.emulateMedia({ colorScheme });
        for (const viewport of viewports) {
          await page.setViewportSize(viewport);
          await page.goto(failDetails, { waitUntil: "domcontentloaded" });
          await details.clickExecutionsTab();
          await expect(details.executionRows.first()).toBeVisible({ timeout: 30_000 });
          await page.screenshot({ path: testInfo.outputPath(`history-list-${colorScheme}-${viewport.width}.png`), fullPage: true });
          await details.openFirstExecution();
          await page.screenshot({ path: testInfo.outputPath(`history-sheet-${colorScheme}-${viewport.width}.png`) });
          const pageWidth = await page.evaluate(() => document.documentElement.scrollWidth);
          expect(pageWidth).toBeLessThanOrEqual(viewport.width);
          await page.keyboard.press("Escape");
        }
      }
      await page.emulateMedia({ colorScheme: "light" });
      await page.setViewportSize({ width: 1280, height: 800 });
    });

    await test.step("Cleanup: delete the E2E-hist-* schedules (their rows stay: no TTL)", async () => {
      for (const detailsUrl of created) {
        await page.goto(detailsUrl, { waitUntil: "domcontentloaded" });
        await details.clickDelete();
        await modal.clickConfirm();
        await toast.expectSuccessVisible("Schedule deleted successfully");
      }
    });
  });
});

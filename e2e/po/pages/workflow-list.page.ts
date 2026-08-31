import { expect, type Locator, type Page } from "@playwright/test";
import { e2eBaseUrl } from "../../support/env";

/**
 * Workflow list page `/app/:itemId/workflow`.
 *
 * Renders either:
 *   - empty state ("Create your first workflow" + "Create workflow" button)
 *   - populated TanStack table with Name / Created on / Last updated / Status
 *     columns + per-row action menu (Open / Rename / Duplicate / Publish or
 *     Unpublish / Delete). The Delete menu opens the shared delete dialog.
 *
 * The toolbar has Status All button (popover with radio items: All,
 * Published, Unpublished) and a search textbox.
 */
export class WorkflowListPage {
  constructor(private readonly page: Page) {}

  async gotoList(itemId: string): Promise<void> {
    await this.page.goto(`${e2eBaseUrl()}/app/${itemId}/workflow`, {
      waitUntil: "domcontentloaded",
    });
  }

  get heading(): Locator {
    return this.page.getByRole("heading", { name: "Workflow", exact: true });
  }

  async expectHeadingVisible(timeout = 30_000): Promise<void> {
    await expect(this.heading).toBeVisible({ timeout });
  }

  // ---- Toolbar + actions ---------------------------------------------------

  get statusFilter(): Locator {
    return this.page.getByRole("button", { name: /^Status/ }).first();
  }

  get addWorkflowButton(): Locator {
    return this.page.getByRole("button", { name: "Add Workflow" });
  }

  /** Empty-state "Create workflow" button (renders only when 0 workflows). */
  get createWorkflowEmptyButton(): Locator {
    return this.page.getByRole("button", { name: "Create workflow" });
  }

  async clickAddWorkflow(): Promise<boolean> {
    // The toolbar's "Add Workflow" button only mounts in the populated state.
    // In empty state ("Create your first workflow") the page renders a
    // different button labeled "Create workflow". Both buttons open the same
    // create-workflow dialog, so abstract the difference here — callers don't
    // have to defensively check `isEmptyState` themselves.
    const empty = await this.isEmptyState();
    if (empty) {
      await expect(this.createWorkflowEmptyButton).toBeVisible({ timeout: 5_000 });
      await this.createWorkflowEmptyButton.click();
    } else {
      await expect(this.addWorkflowButton).toBeVisible({ timeout: 30_000 });
      await this.addWorkflowButton.click();
    }
    return true;
  }

  // ---- Empty state ---------------------------------------------------------

  get emptyHeading(): Locator {
    return this.page.getByRole("heading", { name: "Create your first workflow" });
  }

  async isEmptyState(): Promise<boolean> {
    return this.emptyHeading.isVisible({ timeout: 8_000 }).catch(() => false);
  }

  // ---- Column headers ------------------------------------------------------

  async expectColumnHeaders(): Promise<void> {
    await expect(this.page.getByRole("columnheader", { name: "Name" })).toBeVisible();
    await expect(this.page.getByRole("columnheader", { name: "Created on" })).toBeVisible();
    await expect(this.page.getByRole("columnheader", { name: "Last updated" })).toBeVisible();
    await expect(this.page.getByRole("columnheader", { name: "Status" })).toBeVisible();
  }

  // ---- Search --------------------------------------------------------------

  get searchInput(): Locator {
    return this.page.getByRole("textbox").first();
  }

  async searchBy(text: string): Promise<void> {
    await this.searchInput.fill(text);
  }

  // ---- Status filter (radio popover) ---------------------------------------

  /**
   * Open the Status popover and click a radio. The popover contains a Radix
   * <RadioGroup> wrapped in a <Label htmlFor>; clicking the Label text
   * toggles the corresponding RadioGroupItem via the htmlFor association.
   *
   * Sequential calls (Published → Unpublished → All) used to flake because
   * the popover didn't always re-open cleanly between selections. We now:
   *   (a) wait for the heading to be visible (proves we're still on the
   *       workflow list — guards against stale URL after a previous step),
   *   (b) force-click the trigger (the button can be scrolled out during
   *       table refresh), wait for any radio to appear,
   *   (c) click the Label text inside the open popover (scoped to the
   *       popover content so we don't hit a radio elsewhere on the page),
   *   (d) wait for the popover to dismiss, press Escape as a belt-and-
   *       braces close, then wait for the heading to re-render.
   */
  async selectStatus(name: string, exact = false): Promise<boolean> {
    // (a) bail early — after a filter that returns zero rows the workflow
    // list transitions to the empty state, which unmounts the
    // FilterToolbar entirely. The next selection has nothing to toggle.
    if (!(await this.statusFilter.isVisible({ timeout: 2_000 }).catch(() => false))) {
      return false;
    }
    await this.expectHeadingVisible();
    // The FilterToolbar can briefly unmount during a table refetch triggered
    // by the previous selection — wait for the trigger to settle before
    // trying to click. 30s gives it generous breathing room.
    await this.statusFilter.waitFor({ state: "visible", timeout: 30_000 });
    await this.statusFilter.scrollIntoViewIfNeeded();
    await this.statusFilter.click({ force: true });
    // (b) wait for the popover to actually open by polling for any radio
    await this.page.getByRole("radio").first().waitFor({ state: "visible", timeout: 10_000 });
    // (c) click the visible <span>{option.label}</span> inside the Label.
    const popover = this.page
      .locator('[role="dialog"], [data-radix-popper-content-wrapper]')
      .last();
    const option = popover.getByText(name, { exact });
    await option.waitFor({ state: "visible" });
    await option.click({ force: true });
    // (d) wait for the popover to dismiss; press Escape as a fallback
    await this.page
      .getByRole("radio")
      .first()
      .waitFor({ state: "hidden", timeout: 5_000 })
      .catch(() => {});
    await this.page.keyboard.press("Escape").catch(() => {});
    await this.expectHeadingVisible();
    await this.page.waitForLoadState("networkidle").catch(() => {});
    return true;
  }

  // ---- Pagination ----------------------------------------------------------

  get pageSizeSelect(): Locator {
    return this.page.getByRole("combobox");
  }

  async setPageSize(size: number): Promise<void> {
    await this.pageSizeSelect.click();
    await this.page.getByRole("option", { name: String(size) }).click();
  }

  get pageIndicator(): Locator {
    return this.page.getByText(/Page 1 of/);
  }

  // ---- Rows + action menu --------------------------------------------------

  get firstDataRow(): Locator {
    return this.page.getByRole("row").nth(1);
  }

  /** Trigger button (EllipsisVertical icon) inside a row. */
  rowMenuTrigger(row: Locator): Locator {
    return row.getByRole("button").last();
  }

  async openFirstRowMenu(): Promise<void> {
    await this.rowMenuTrigger(this.firstDataRow).click();
  }

  async openRowMenu(row: Locator): Promise<void> {
    await this.rowMenuTrigger(row).click();
  }

  /** Click a menu item by its visible label. */
  async clickRowMenuItem(name: string): Promise<void> {
    await this.page.getByText(name, { exact: true }).click();
  }

  /** Assert the row action menu contains the given items. */
  async expectMenuItems(items: { name: string; exact?: boolean }[]): Promise<void> {
    for (const item of items) {
      const matcher = this.page.getByText(item.name, { exact: item.exact ?? true });
      if (item.name === "Publish") {
        await expect(matcher.or(this.page.getByText("Unpublish", { exact: true }))).toBeVisible();
      } else {
        await expect(matcher).toBeVisible();
      }
    }
  }

  async firstRowName(): Promise<string> {
    return (await this.firstDataRow.locator("td").first().innerText()).trim();
  }

  async hasFirstRow(): Promise<boolean> {
    return this.firstDataRow.isVisible({ timeout: 5_000 }).catch(() => false);
  }

  async expectFirstRowContains(text: string): Promise<void> {
    await expect(this.firstDataRow).toContainText(text);
  }

  // ---- Row-level status text ----------------------------------------------

  async rowHasText(row: Locator, text: string, exact = true): Promise<boolean> {
    return row
      .getByText(text, { exact })
      .isVisible()
      .catch(() => false);
  }

  /** Toggle Publish/Unpublish switch in a row (used by publish confirmation flows). */
  rowPublishSwitch(row: Locator): Locator {
    return row.locator('button[role="switch"]');
  }

  // ---- Publish/Unpublish dialogs -------------------------------------------

  get publishConfirmHeading(): Locator {
    return this.page.getByRole("heading", { name: "Publish workflow?" });
  }

  get publishNamedHeading(): Locator {
    return this.page.getByRole("heading", { name: "Publish workflow" });
  }

  get unpublishConfirmHeading(): Locator {
    return this.page.getByRole("heading", { name: "Unpublish workflow?" });
  }

  async expectPublishConfirmation(): Promise<void> {
    await expect(this.publishConfirmHeading.or(this.publishNamedHeading)).toBeVisible();
  }

  async expectUnpublishConfirmation(): Promise<void> {
    await expect(this.unpublishConfirmHeading).toBeVisible();
  }

  get versionNameField(): Locator {
    return this.page.getByLabel(/Version name/);
  }

  async expectNamedVersionDialog(): Promise<void> {
    if (await this.publishNamedHeading.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await expect(this.versionNameField).toHaveValue(/^Version /);
    }
  }

  // ---- Create / Rename / Duplicate dialogs ---------------------------------

  get createWorkflowHeading(): Locator {
    return this.page.getByRole("heading", { name: "Create workflow" });
  }

  get renameWorkflowHeading(): Locator {
    return this.page.getByRole("heading", { name: "Rename workflow" });
  }

  get duplicateWorkflowHeading(): Locator {
    return this.page.getByRole("heading", { name: "Duplicate workflow" });
  }

  get deleteWorkflowHeading(): Locator {
    return this.page.getByRole("heading", { name: "Delete Workflow" });
  }

  get workflowNameInput(): Locator {
    return this.page.getByLabel("Workflow Name");
  }

  get createWorkflowSubmit(): Locator {
    return this.page.getByRole("button", { name: "Create" });
  }

  get cancelButton(): Locator {
    return this.page.getByRole("button", { name: "Cancel" });
  }

  get renameSubmitButton(): Locator {
    return this.page.getByRole("button", { name: "Rename" });
  }

  /**
   * Duplicate submission: the dialog's submit button label varies between
   * "Save" / "Duplicate" / "Create" — caller passes the right regex.
   */
  duplicateSubmitButton(): Locator {
    return this.page.getByRole("button", { name: /save|duplicate|create/i }).last();
  }

  get closeButton(): Locator {
    return this.page.getByRole("button", { name: "Close" });
  }

  async expectCreateDialogVisible(): Promise<void> {
    await expect(this.createWorkflowHeading).toBeVisible();
    await expect(this.workflowNameInput).toBeVisible();
  }

  async expectCreateDialogHidden(): Promise<void> {
    await expect(this.createWorkflowHeading).toBeHidden();
  }

  async expectRenameDialogVisible(): Promise<void> {
    await expect(this.renameWorkflowHeading).toBeVisible();
  }

  async expectDuplicateDialogVisible(): Promise<void> {
    await expect(this.duplicateWorkflowHeading).toBeVisible();
    await expect(this.workflowNameInput).not.toHaveValue("");
  }

  async expectDeleteWorkflowDialogVisible(): Promise<void> {
    await expect(this.deleteWorkflowHeading).toBeVisible();
    await expect(
      this.page.getByText("Are you sure you want to delete this workflow?"),
    ).toBeVisible();
  }
}

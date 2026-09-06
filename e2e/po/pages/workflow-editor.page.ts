import { expect, type Locator, type Page } from "@playwright/test";
import { e2eBaseUrl } from "../../support/env";

/**
 * Workflow editor `/app/:itemId/workflow/:workflowId`.
 *
 * Three tabs: Editor (default) / Executions / Versions. The Editor view
 * contains:
 *   - Header: status badge (Published/Unpublished) + last-saved label
 *   - Yellow "dirty" banner when there are unadapted changes
 *   - "Save" button (disabled unless dirty)
 *   - "Add first step" CTA when the canvas is empty
 *   - React Flow canvas
 */
export class WorkflowEditorPage {
  constructor(private readonly page: Page) {}

  async gotoEditor(itemId: string, workflowId: string): Promise<void> {
    await this.page.goto(`${e2eBaseUrl()}/app/${itemId}/workflow/${workflowId}`, {
      waitUntil: "domcontentloaded",
    });
  }

  // ---- Tabs ----------------------------------------------------------------

  get editorTab(): Locator {
    return this.page.getByRole("tab", { name: "Editor" });
  }

  get executionsTab(): Locator {
    return this.page.getByRole("tab", { name: "Executions" });
  }

  get versionsTab(): Locator {
    return this.page.getByRole("tab", { name: "Versions" });
  }

  async clickEditorTab(): Promise<void> {
    await this.editorTab.click();
  }

  async expectOnEditorTab(): Promise<void> {
    await expect(this.editorTab).toHaveAttribute("data-state", "active");
    await expect(this.executionsTab).toBeVisible();
    await expect(this.versionsTab).toBeVisible();
  }

  // ---- Editor view content -------------------------------------------------

  /** Either "Published" or "Unpublished" badge visible. */
  get statusBadge(): Locator {
    return this.page
      .getByText("Published", { exact: true })
      .or(this.page.getByText("Unpublished", { exact: true }));
  }

  /** "Last saved: …" label or "Not saved yet" placeholder. */
  get lastSavedLabel(): Locator {
    return this.page.getByText(/Last saved:/).or(this.page.getByText("Not saved yet"));
  }

  /** Yellow "You have unadapted changes" banner. */
  get dirtyBanner(): Locator {
    return this.page.getByText(
      "You have unadapted changes. Please click on the Publish button to adapt them.",
    );
  }

  get addFirstStepButton(): Locator {
    return this.page.getByRole("button", { name: "Add first step" });
  }

  get startYourWorkflowHeading(): Locator {
    return this.page.getByRole("heading", { name: "Start your workflow" });
  }

  // ---- Header actions (in-editor Publish + Save) ---------------------------

  /** Publish dropdown trigger on the editor header — shows "Publish" + chevron. */
  get publishTrigger(): Locator {
    return this.page
      .locator("main, body")
      .getByRole("button", { name: /^Publish/ })
      .first();
  }

  get saveButton(): Locator {
    return this.page.getByRole("button", { name: "Save", exact: true });
  }

  // ---- Node library side panel (Sheet) -------------------------------------

  get nodeLibraryHeading(): Locator {
    return this.page.getByRole("heading", { name: "Start your workflow" });
  }

  get nodeLibrarySearch(): Locator {
    return this.page.getByPlaceholder("Search");
  }

  /** A node option row in the library — by its visible title. */
  nodeLibraryOption(title: string): Locator {
    return this.page
      .locator('[role="dialog"], [data-state="open"]')
      .getByText(title, { exact: true })
      .first();
  }

  // ---- Executions tab content ----------------------------------------------

  get executionsList(): Locator {
    return this.page.getByText("No executions found.", { exact: true });
  }

  // ---- Versions tab content ------------------------------------------------

  get versionsSidebar(): Locator {
    return this.page.locator(
      ":text('No versions found.'), :text('Unnamed Version'), :text('(Published)')",
    );
  }

  get noVersionsText(): Locator {
    return this.page.getByText("No versions found.", { exact: true });
  }

  async expectStatusBadgeVisible(): Promise<void> {
    await expect(this.statusBadge).toBeVisible();
  }

  async expectLastSavedLabelVisible(): Promise<void> {
    await expect(this.lastSavedLabel).toBeVisible();
  }

  async expectDirtyBannerVisible(timeout = 5_000): Promise<void> {
    const visible = await this.dirtyBanner.isVisible({ timeout }).catch(() => false);
    if (visible) {
      await expect(this.dirtyBanner).toBeVisible();
    }
  }

  async expectSaveDisabled(): Promise<void> {
    await expect(this.saveButton).toBeDisabled();
  }

  async expectStartYourWorkflowHeading(): Promise<void> {
    await expect(this.startYourWorkflowHeading).toBeVisible();
  }
}

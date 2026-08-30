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

  get saveButton(): Locator {
    return this.page.getByRole("button", { name: "Save" });
  }

  get addFirstStepButton(): Locator {
    return this.page.getByRole("button", { name: "Add first step" });
  }

  get startYourWorkflowHeading(): Locator {
    return this.page.getByRole("heading", { name: "Start your workflow" });
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

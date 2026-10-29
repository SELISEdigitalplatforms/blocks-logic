import { expect, type Locator, type Page } from "@playwright/test";
import { WorkflowEditorPage } from "./workflow-editor.page";

/**
 * Workflow details → "Executions" tab (workflow-execution/workflow-execution.tsx).
 *
 *   - Left: run list (workflow-execution-list.tsx), newest first, polled every 5 s. Each row shows
 *     the date and, once the run is Completed or Failed, "Completed in N seconds". Rows have no
 *     role or test id: they are the `div.cursor-pointer.shadow` entries (loading skeleton rows use
 *     `shadow-sm`, so they do not match).
 *   - Right: execution view (workflow-execution-editor.tsx): read-only canvas, "Status: <label>"
 *     badge, "Execution Logs" button (data-testid execution-logs-button) opening the logs sheet
 *     (execution-logs-panel.tsx, data-testid execution-logs-panel / execution-log-row).
 */
export class WorkflowExecutionsPage {
  private readonly editor: WorkflowEditorPage;

  constructor(private readonly page: Page) {
    this.editor = new WorkflowEditorPage(page);
  }

  get panel(): Locator {
    return this.page.getByRole("tabpanel", { name: "Executions" });
  }

  get runRows(): Locator {
    return this.panel.locator("div.cursor-pointer.shadow");
  }

  async open(): Promise<void> {
    await this.editor.executionsTab.click();
    await expect(this.editor.executionsTab).toHaveAttribute("data-state", "active");
  }

  /** Wait until the list holds `count` runs (new runs are prepended by the 5 s poll). */
  async waitForRunCount(count: number): Promise<void> {
    await expect(this.runRows).toHaveCount(count, { timeout: 90_000 });
  }

  /** A run row is finished (Completed or Failed) once it shows "Completed in …". */
  async waitForFinished(row: Locator): Promise<void> {
    await expect(row).toContainText("Completed in", { timeout: 180_000 });
  }

  /** "Status:" badge of the open execution. */
  get statusBadge(): Locator {
    return this.panel.getByText("Status:", { exact: true }).locator("..");
  }

  async openRun(row: Locator): Promise<void> {
    await row.click();
    await expect(this.statusBadge).toBeVisible({ timeout: 30_000 });
  }

  async expectStatus(label: "Completed" | "Failed"): Promise<void> {
    await expect(this.statusBadge).toContainText(label, { timeout: 30_000 });
  }

  canvasNode(name: string): Locator {
    return this.editor.canvasNode(name, this.panel);
  }

  /** Click a node of the open execution: opens the (read-only) node inspector. */
  async openNode(name: string): Promise<void> {
    await this.editor.clickCanvasNode(name, this.panel);
  }

  // ---- Execution logs -----------------------------------------------------------

  get logsButton(): Locator {
    return this.panel.getByTestId("execution-logs-button");
  }

  get logsPanel(): Locator {
    return this.page.getByTestId("execution-logs-panel");
  }

  get logRows(): Locator {
    return this.logsPanel.getByTestId("execution-log-row");
  }

  /** Log rows of one node: the row's node chip is a button "<name>" or "<name>#<run>". */
  nodeLogRows(nodeName: string): Locator {
    const escaped = nodeName.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    return this.logRows.filter({
      has: this.page.getByRole("button", { name: new RegExp(`^${escaped}(#\\d+)?$`) }),
    });
  }

  async openLogs(): Promise<void> {
    if ((await this.logsButton.getAttribute("aria-pressed")) !== "true") {
      await this.logsButton.click();
    }
    await expect(this.logsPanel).toBeVisible();
  }

  async closeLogs(): Promise<void> {
    if (await this.logsPanel.isVisible()) {
      await this.logsPanel.getByRole("button", { name: "Close execution logs" }).click();
      await expect(this.logsPanel).toBeHidden();
    }
  }

  /**
   * Wait for a log line (logs can arrive late: the panel says "Waiting for logs…"), pressing
   * "Refresh logs" between checks. With `stage`, the row must also carry that stage (the stage
   * icon's title attribute).
   */
  async expectLogLine(text: string, stage?: string): Promise<Locator> {
    let line = this.logRows.filter({ hasText: text });
    if (stage) line = line.filter({ has: this.page.locator(`span[title="${stage}"]`) });
    await expect(async () => {
      if ((await line.count()) === 0) {
        await this.logsPanel.getByRole("button", { name: "Refresh logs" }).click();
      }
      await expect(line).toHaveCount(1, { timeout: 3_000 });
    }).toPass({ timeout: 90_000, intervals: [2_000, 5_000] });
    return line;
  }
}

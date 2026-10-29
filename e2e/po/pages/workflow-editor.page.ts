import fs from "fs";
import { expect, type Locator, type Page, type Response } from "@playwright/test";
import { e2eBaseUrl } from "../../support/env";

/** One node of an exported workflow file (client utils/workflow-export.ts). */
export type ExportedWorkflowNode = {
  id: string;
  name: string;
  category: string;
  type: string;
  version: string;
  handle?: {
    sources?: { key: string; label?: string }[];
    targets?: { key: string; label?: string }[];
  };
  parameters: Record<string, unknown>;
  settings: Record<string, unknown>;
};

export type ExportedWorkflow = {
  name: string;
  nodes: ExportedWorkflowNode[];
  edges: { source: string; target: string; sourceHandle: string; targetHandle: string }[];
};

const isWorkflowUpdate = (response: Response) =>
  response.request().method() === "PUT" && /\/Workflow\/Update(?:\?|$)/.test(response.url());

const isTriggerListener = (response: Response) =>
  response.request().method() === "POST" &&
  /\/Workflow\/TriggerListener(?:\?|$)/.test(response.url());

/**
 * Workflow editor `/app/:itemId/workflow/:workflowId`.
 *
 * Three tabs: Editor (default) / Executions / Versions. The Editor view
 * contains:
 *   - Header: publish-status button (Published/Publish) + last-saved label
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

  /**
   * Publish status in the Editor header. Since e06dc579 the separate
   * Published/Unpublished text badge is gone; the split publish action's main
   * button carries the status instead: "Published" (green dot) when published
   * and clean, "Publish" otherwise.
   */
  get statusBadge(): Locator {
    return this.page.getByRole("button", { name: /^(Published|Publish)$/ }).first();
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

  // ---- Canvas (React Flow) ---------------------------------------------------
  //
  // xyflow renders each node as `.react-flow__node[data-id=<node id>]`; editor-node-base.tsx puts
  // the node's name in an <h4> under the card. Handles carry `.react-flow__handle.source|target`
  // and `data-handleid`; edges carry aria-label "Edge from <source id> to <target id>".

  get editorPanel(): Locator {
    return this.page.getByRole("tabpanel", { name: "Editor" });
  }

  /** A canvas node by its name (the <h4> under the card), inside `scope` (default: Editor tab). */
  canvasNode(name: string, scope: Locator = this.editorPanel): Locator {
    return scope
      .locator(".react-flow__node")
      .filter({ has: this.page.getByRole("heading", { level: 4, name, exact: true }) });
  }

  sourceHandles(node: Locator): Locator {
    return node.locator(".react-flow__handle.source");
  }

  targetHandles(node: Locator): Locator {
    return node.locator(".react-flow__handle.target");
  }

  async nodeId(name: string, scope: Locator = this.editorPanel): Promise<string> {
    const id = await this.canvasNode(name, scope).getAttribute("data-id");
    if (!id) throw new Error(`Canvas node "${name}" has no data-id`);
    return id;
  }

  edgeBetween(sourceId: string, targetId: string, scope: Locator = this.editorPanel): Locator {
    return scope.locator(`.react-flow__edge[aria-label="Edge from ${sourceId} to ${targetId}"]`);
  }

  // ---- Canvas controls (workflow-editor-controls.tsx: icon buttons, names only in tooltips) ----

  controls(scope: Locator = this.editorPanel): Locator {
    return scope.locator(".react-flow__controls");
  }

  fitViewButton(scope: Locator = this.editorPanel): Locator {
    return this.controls(scope).locator("button:has(svg.lucide-maximize)");
  }

  /** "Open Node Library" (Plus icon). */
  get openNodeLibraryControl(): Locator {
    return this.controls().locator("button:has(svg.lucide-plus)");
  }

  /** "Execute Workflow" (Play icon): saves the workflow, then turns the trigger listener on. */
  get executeWorkflowButton(): Locator {
    return this.controls().locator("button:has(svg.lucide-play)");
  }

  /** "Stop Workflow" (Square icon), shown while the trigger listener is on. */
  get stopWorkflowButton(): Locator {
    return this.controls().locator("button:has(svg.lucide-square)");
  }

  /** Click a canvas node; fits the view first when the node is outside the visible canvas. */
  async clickCanvasNode(name: string, scope: Locator = this.editorPanel): Promise<void> {
    const node = this.canvasNode(name, scope);
    await expect(node).toHaveCount(1);
    const inView = await expect(node)
      .toBeInViewport({ timeout: 1_000 })
      .then(() => true)
      .catch(() => false);
    if (!inView) {
      await this.fitViewButton(scope).click();
      await expect(node).toBeInViewport({ timeout: 5_000 });
    }
    await node.click();
  }

  /**
   * Draw an edge from `sourceName`'s output handle to `targetName`'s input handle with the mouse
   * (xyflow starts a connection on pointer-down on a handle and connects on release over another).
   */
  async connectNodes(sourceName: string, targetName: string): Promise<void> {
    const sourceHandle = this.sourceHandles(this.canvasNode(sourceName)).first();
    const targetHandle = this.targetHandles(this.canvasNode(targetName)).first();
    await expect(sourceHandle).toBeInViewport();
    await expect(targetHandle).toBeInViewport();
    const from = await sourceHandle.boundingBox();
    const to = await targetHandle.boundingBox();
    if (!from || !to) throw new Error(`No handle box for ${sourceName} → ${targetName}`);
    await this.page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
    await this.page.mouse.down();
    await this.page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 20 });
    await this.page.mouse.up();
  }

  // ---- Node library (node-library-panel.tsx) ---------------------------------------

  get nodeLibrarySheet(): Locator {
    return this.page
      .getByRole("dialog")
      .filter({ has: this.page.getByPlaceholder("Search", { exact: true }) });
  }

  /** One library entry: a clickable row whose <h4> is the node title. */
  libraryEntry(title: string): Locator {
    return this.nodeLibrarySheet
      .locator("div.cursor-pointer")
      .filter({ has: this.page.getByRole("heading", { level: 4, name: title, exact: true }) });
  }

  /**
   * Open the library from a node's unconnected output: the "+" at the end of its handle arrow
   * (editor-node-handle.tsx EditorNodeHandleArrow) selects the node and that handle, so the next
   * node picked is connected to it.
   */
  async openLibraryFromNodeOutput(name: string): Promise<void> {
    await this.sourceHandles(this.canvasNode(name)).locator("svg.lucide-plus").first().click();
    await expect(this.nodeLibrarySheet).toBeVisible();
  }

  async openLibraryFromControls(): Promise<void> {
    await this.openNodeLibraryControl.click();
    await expect(this.nodeLibrarySheet).toBeVisible();
  }

  // ---- Header actions -------------------------------------------------------------

  /** The header ⋮ menu (Rename / Duplicate / Export / Delete); its trigger sits right after Save. */
  async openHeaderMenu(): Promise<void> {
    await this.saveButton.locator("xpath=following-sibling::button[1]").click();
    await expect(this.page.getByRole("menuitem", { name: "Export" })).toBeVisible();
  }

  async saveWorkflow(): Promise<void> {
    await expect(this.saveButton).toBeEnabled();
    const update = this.page.waitForResponse(isWorkflowUpdate);
    await this.saveButton.click();
    expect((await update).ok(), "Workflow/Update failed").toBeTruthy();
    await expect(this.saveButton).toBeDisabled({ timeout: 15_000 });
  }

  /** Header ⋮ → Export: downloads the saved workflow (use-export-workflow.ts) and parses it. */
  async exportWorkflow(): Promise<ExportedWorkflow> {
    await this.openHeaderMenu();
    const downloadPromise = this.page.waitForEvent("download");
    await this.page.getByRole("menuitem", { name: "Export" }).click();
    const download = await downloadPromise;
    const filePath = await download.path();
    return JSON.parse(fs.readFileSync(filePath, "utf8")) as ExportedWorkflow;
  }

  /**
   * Turn the test listener on with "Execute Workflow" (which also saves the workflow). A listener
   * still on from an earlier run (its completion notification not received here) is stopped first,
   * so every run starts from a fresh save.
   */
  async startTestListener(): Promise<void> {
    if (await this.stopWorkflowButton.isVisible()) {
      // The listener can finish on its own between the check and the click; the wait below is the
      // real check that the play button is back.
      await this.stopWorkflowButton.click({ timeout: 5_000 }).catch(() => undefined);
    }
    await expect(this.executeWorkflowButton).toBeEnabled({ timeout: 30_000 });

    const update = this.page.waitForResponse(isWorkflowUpdate);
    const listener = this.page.waitForResponse(isTriggerListener);
    await this.executeWorkflowButton.click();
    expect((await update).ok(), "Workflow/Update failed").toBeTruthy();
    expect((await listener).ok(), "Workflow/TriggerListener failed").toBeTruthy();
    await expect(this.stopWorkflowButton).toBeVisible({ timeout: 15_000 });
  }
}

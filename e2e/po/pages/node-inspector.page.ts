import { expect, type Locator, type Page } from "@playwright/test";

/**
 * Node inspector side sheet (node-inspector/node-inspector.tsx), opened by clicking a canvas node
 * in the editor or in an execution view. Layouts: layout-with-io.tsx (Input + Output panels) and
 * layout-with-listener.tsx (Webhook: Output panel only). Both have Parameters / Settings tabs.
 *
 * The Filter node's Parameters tab is filter-conditions-field.tsx → switch-condition-list.tsx:
 *   - "Conditions" heading and a role=group "Match" with AND / OR buttons (aria-pressed)
 *   - per condition n: textboxes "Condition n value to check" / "Condition n value to compare with",
 *     picker button "Condition n type and operator" (type sub-menus → operator items),
 *     "Remove condition n" (only when there are 2+ conditions), invalid-literal hint (role=status)
 *   - "Add condition"
 */
export class NodeInspectorPage {
  constructor(private readonly page: Page) {}

  get dialog(): Locator {
    return this.page
      .getByRole("dialog")
      .filter({ has: this.page.getByRole("tab", { name: "Parameters", exact: true }) });
  }

  /** SheetTitle (h2) holds the node icon, its name and the rename button. */
  get title(): Locator {
    return this.dialog.getByRole("heading", { level: 2 }).first();
  }

  async expectOpenFor(nodeName: string): Promise<void> {
    await expect(this.dialog).toBeVisible();
    await expect(this.title).toContainText(nodeName);
  }

  /** Header close button (X icon, no accessible name) → closeConfigModal. */
  async close(): Promise<void> {
    await this.dialog.locator("button:has(svg.lucide-x)").first().click();
    await expect(this.dialog).toBeHidden();
  }

  async closeIfOpen(): Promise<void> {
    if (await this.dialog.isVisible()) await this.close();
  }

  // ---- Filter conditions ------------------------------------------------------------

  get conditionsHeading(): Locator {
    return this.dialog.getByText("Conditions", { exact: true });
  }

  get matchGroup(): Locator {
    return this.dialog.getByRole("group", { name: "Match" });
  }

  get andButton(): Locator {
    return this.matchGroup.getByRole("button", { name: "AND", exact: true });
  }

  get orButton(): Locator {
    return this.matchGroup.getByRole("button", { name: "OR", exact: true });
  }

  valueToCheck(n: number): Locator {
    return this.dialog.getByRole("textbox", { name: `Condition ${n} value to check`, exact: true });
  }

  valueToCompare(n: number): Locator {
    return this.dialog.getByRole("textbox", {
      name: `Condition ${n} value to compare with`,
      exact: true,
    });
  }

  typeAndOperatorPicker(n: number): Locator {
    return this.dialog.getByRole("button", {
      name: `Condition ${n} type and operator`,
      exact: true,
    });
  }

  removeConditionButton(n: number): Locator {
    return this.dialog.getByRole("button", { name: `Remove condition ${n}`, exact: true });
  }

  get addConditionButton(): Locator {
    return this.dialog.getByRole("button", { name: "Add condition" });
  }

  hint(text: string): Locator {
    return this.dialog.getByRole("status").filter({ hasText: text });
  }

  /** Picker → type sub-menu (e.g. "Number") → operator item (e.g. "Greater than"). */
  async chooseTypeAndOperator(n: number, typeLabel: string, operatorLabel: string): Promise<void> {
    await this.typeAndOperatorPicker(n).click();
    await this.page.getByRole("menuitem", { name: typeLabel, exact: true }).click();
    await this.page.getByRole("menuitem", { name: operatorLabel, exact: true }).click();
    await expect(this.page.getByRole("menu")).toHaveCount(0);
    await expect(this.typeAndOperatorPicker(n)).toContainText(typeLabel);
    await expect(this.typeAndOperatorPicker(n)).toContainText(operatorLabel.toLowerCase());
  }

  /** The remove button only shows on hover / focus (opacity), so hover the condition first. */
  async removeCondition(n: number): Promise<void> {
    await this.valueToCheck(n).hover();
    await this.removeConditionButton(n).click();
  }

  async expectCondition(
    n: number,
    expected: { left: string; right?: string; typeLabel: string; operatorLabel: string },
  ): Promise<void> {
    await expect(this.valueToCheck(n)).toHaveValue(expected.left);
    if (expected.right !== undefined) {
      await expect(this.valueToCompare(n)).toHaveValue(expected.right);
    }
    await expect(this.typeAndOperatorPicker(n)).toContainText(expected.typeLabel);
    await expect(this.typeAndOperatorPicker(n)).toContainText(expected.operatorLabel.toLowerCase());
  }

  async expectMatch(mode: "AND" | "OR"): Promise<void> {
    await expect(this.andButton).toHaveAttribute("aria-pressed", String(mode === "AND"));
    await expect(this.orButton).toHaveAttribute("aria-pressed", String(mode === "OR"));
  }

  // ---- Webhook -------------------------------------------------------------------

  /**
   * "Deprecated Webhook URL" (node-schema-trigger-webhook-v1.ts, tab-with-text field id
   * "webhook-url-deprecated"): `<logic api>/Workflow/webhook-test/<tenantId>/<workflowId>/<nodeId>`
   * in the editor (Test mode). The tenant is in the path, so no x-blocks-key header is needed.
   */
  get deprecatedWebhookUrlInput(): Locator {
    return this.dialog.locator("#webhook-url-deprecated");
  }

  // ---- Guide (node-inspector-header.tsx + shared/node-guide-panel.tsx) ---------------

  get guideButton(): Locator {
    return this.dialog.getByRole("button", { name: "Guide", exact: true });
  }

  get guidePanelHeading(): Locator {
    return this.dialog.getByRole("heading", { name: "Guide", level: 3 });
  }

  get hideGuideButton(): Locator {
    return this.dialog.getByRole("button", { name: "Hide guide" });
  }

  // ---- Output panel (shared/output-panel/output-panel.tsx) ---------------------------

  /** Error of the node's latest failed run (role=alert in the Output panel). */
  get failedRunAlert(): Locator {
    return this.dialog.getByRole("alert");
  }

  /**
   * The rows the node output on the shown run (each item's `Output`), read from the Output panel's
   * JSON tab. The Input panel (layout-with-io.tsx) is collapsed first so only the output's JSON is
   * on screen; the Output panel's JSON tab is the last "JSON" tab in the sheet.
   */
  async readOutputRows(): Promise<unknown[]> {
    const collapseInput = this.dialog.getByRole("button", { name: "Collapse input" });
    if (await collapseInput.isVisible()) await collapseInput.click();

    await this.dialog.getByRole("tab", { name: "JSON", exact: true }).last().click();
    const empty = this.dialog.getByText("No runtime output data available.");
    const code = this.dialog.locator("pre code");
    await expect(empty.or(code).first()).toBeVisible();
    if ((await code.count()) === 0) return [];
    return JSON.parse((await code.last().textContent()) ?? "[]") as unknown[];
  }
}

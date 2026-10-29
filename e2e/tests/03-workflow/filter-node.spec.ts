import { test, expect } from "../../support/test-base";
import {
  E1_ITEMS,
  NODE_NAMES,
  joinWorkflowFile,
  mainWorkflowFile,
  startsWithWorkflowFile,
} from "../../support/filter-fixtures";
import {
  deleteWorkflowAt,
  deleteWorkflowNamed,
  importWorkflowFile,
  type ImportedWorkflow,
} from "../../support/workflow-import";
import {
  NodeInspectorPage,
  WorkflowEditorPage,
  WorkflowExecutionsPage,
  type ExportedWorkflow,
  type ExportedWorkflowNode,
} from "../../po";

/**
 * Filter node (logic / filter / v2) — spec blocks-logic-377 §10 steps 1–11.
 *
 * Fixtures are imported through the workflow list's Import control (support/filter-fixtures.ts),
 * so no canvas drag-and-drop is needed to build them. The criteria themselves are driven through
 * the UI: node library, canvas, inspector, Save, reload, Export, Execute Workflow, Executions tab.
 * Test runs POST the E1 body to the Webhook node's test URL (read from its inspector). Every
 * workflow the spec creates is deleted in the `finally` block, also when a step fails.
 *
 * Runs in the shared suite project ("test"): openWorkflowList() opens that project's list.
 */

const {
  webhook: WEBHOOK,
  setField: SET_FIELD,
  filter: FILTER,
  after: AFTER,
  merge: MERGE,
} = NODE_NAMES;

const FILTER_2 = "Filter2"; // node-library-panel.tsx names the 2nd Filter `${defaultName}${count + 1}`

const STARTS_WITH_FAILURE =
  "Condition could not be evaluated for 3 item(s). First: Item 0: Unknown operator starts_with for type string";

function nodeNamed(workflow: ExportedWorkflow, name: string): ExportedWorkflowNode {
  const node = workflow.nodes.find((candidate) => candidate.name === name);
  if (!node) throw new Error(`Exported workflow has no node named "${name}"`);
  return node;
}

test.describe("workflow — Filter node", () => {
  test("Filter node: library, inspector, saved shape, test runs, read-only view, guide and join", async ({
    page,
  }) => {
    test.setTimeout(25 * 60_000);

    const editor = new WorkflowEditorPage(page);
    const inspector = new NodeInspectorPage(page);
    const executions = new WorkflowExecutionsPage(page);

    const stamp = Date.now();
    const created: ImportedWorkflow[] = [];
    // Names of every import started, so cleanup also finds a workflow whose import never reached its editor.
    const importedNames: string[] = [];
    let primaryFailure = false;

    const importTracked = async (...args: Parameters<typeof importWorkflowFile>) => {
      importedNames.push(args[1].name);
      return importWorkflowFile(...args);
    };

    /** Open a node's inspector on the Editor tab. */
    const openInEditor = async (name: string) => {
      await inspector.closeIfOpen();
      await editor.clickCanvasNode(name);
      await inspector.expectOpenFor(name);
    };

    /** Open a node's inspector in the open execution view. */
    const openInExecution = async (name: string) => {
      await executions.closeLogs();
      await inspector.closeIfOpen();
      await executions.openNode(name);
      await inspector.expectOpenFor(name);
    };

    const backToEditor = async () => {
      await executions.closeLogs();
      await inspector.closeIfOpen();
      await editor.editorTab.click();
      await expect(editor.editorTab).toHaveAttribute("data-state", "active");
      await expect(editor.canvasNode(WEBHOOK)).toBeVisible();
    };

    /** The Webhook node's test URL, read from its inspector ("Deprecated Webhook URL", Test tab). */
    const readWebhookTestUrl = async (): Promise<string> => {
      await openInEditor(WEBHOOK);
      const url = await inspector.deprecatedWebhookUrlInput.inputValue();
      expect(url, "webhook test URL with tenant, workflow and node in the path").toMatch(
        /\/Workflow\/webhook-test\/[^/]+\/[^/]+\/[^/]+$/,
      );
      await inspector.close();
      return url;
    };

    /**
     * Execute Workflow (save + listener on), POST the E1 items to the webhook test URL, then open
     * the new run in the Executions tab once it has finished.
     */
    const testRun = async (webhookUrl: string, expectedRunCount: number) => {
      await inspector.closeIfOpen();
      await editor.startTestListener();

      const response = await page.request.post(webhookUrl, { data: E1_ITEMS });
      expect(response.ok(), `webhook-test answered HTTP ${response.status()}`).toBeTruthy();
      const body = (await response.json()) as { executionId?: string | null; status?: string };
      expect(body.executionId, `webhook-test did not start a run: ${body.status}`).toBeTruthy();

      await executions.open();
      await executions.waitForRunCount(expectedRunCount);
      const latest = executions.runRows.first();
      await executions.waitForFinished(latest);
      await executions.openRun(latest);
    };

    try {
      // ---- Fixture ----------------------------------------------------------------------

      const main =
        await test.step("[Setup] Import the §10 fixture: Webhook → Set Field, and After (not connected yet)", async () => {
          const imported = await importTracked(page, mainWorkflowFile(`E2E Filter ${stamp}`));
          created.push(imported);
          await expect(editor.canvasNode(WEBHOOK)).toBeVisible();
          await expect(editor.canvasNode(SET_FIELD)).toBeVisible();
          await expect(editor.canvasNode(AFTER)).toBeVisible();
          await expect(editor.canvasNode(FILTER)).toHaveCount(0);
          return imported;
        });

      const mainWebhookUrl = await test.step(
        "[Setup] Read the Webhook test URL",
        readWebhookTestUrl,
      );

      // ---- §10 step 1 ----------------------------------------------------------------

      await test.step("[H1] Node library lists Filter next to the other Logic nodes, with the Filter icon and its description", async () => {
        await editor.openLibraryFromNodeOutput(SET_FIELD);

        const filterEntry = editor.libraryEntry("Filter");
        await expect(filterEntry).toBeVisible();
        await expect(filterEntry).toContainText("Keep only the items that match the conditions.");
        // node-definitions.tsx: lucide `Filter` icon → <svg class="lucide lucide-filter">.
        await expect(filterEntry.locator("svg.lucide-filter")).toHaveCount(1);

        // The library is one flat list in definition order (no category headings); the Logic
        // definitions (category "logic") are listed together: If, Switch, Filter. The saved
        // category itself is checked on the exported JSON in step 2.
        const titles = (
          await editor.nodeLibrarySheet.getByRole("heading", { level: 4 }).allTextContents()
        ).map((title) => title.trim());
        expect(titles.indexOf("Switch"), "Switch is listed").toBeGreaterThan(-1);
        expect(titles.indexOf("Switch")).toBe(titles.indexOf("If") + 1);
        expect(titles.indexOf("Filter")).toBe(titles.indexOf("Switch") + 1);

        await editor.nodeLibrarySheet.getByPlaceholder("Search", { exact: true }).fill("Filter");
        await expect(filterEntry).toBeVisible();
      });

      await test.step("[H2] Add Filter after Set Field and connect it to After: one target and one source handle on the canvas", async () => {
        await editor.libraryEntry("Filter").click();
        await expect(editor.nodeLibrarySheet).toBeHidden();

        const filterNode = editor.canvasNode(FILTER);
        await expect(filterNode).toBeVisible();
        await expect(editor.targetHandles(filterNode)).toHaveCount(1);
        await expect(editor.sourceHandles(filterNode)).toHaveCount(1);
        await expect(editor.targetHandles(filterNode)).toHaveAttribute("data-handleid", "target");
        await expect(editor.sourceHandles(filterNode)).toHaveAttribute("data-handleid", "source");

        const setFieldId = await editor.nodeId(SET_FIELD);
        const filterId = await editor.nodeId(FILTER);
        const afterId = await editor.nodeId(AFTER);
        // Picked from Set Field's output "+": the library connects Set Field → Filter itself.
        await expect(editor.edgeBetween(setFieldId, filterId)).toHaveCount(1);

        await editor.connectNodes(FILTER, AFTER);
        await expect(editor.edgeBetween(filterId, afterId)).toHaveCount(1);
      });

      await test.step("[H3/H4] Inspector default: AND active, one blank String/equals condition, the condition list only", async () => {
        await openInEditor(FILTER);

        await expect(inspector.conditionsHeading).toHaveCount(1);
        await inspector.expectMatch("AND");
        await inspector.expectCondition(1, {
          left: "",
          right: "",
          typeLabel: "String",
          operatorLabel: "Equals",
        });
        await expect(inspector.valueToCheck(2)).toHaveCount(0);
        await expect(inspector.removeConditionButton(1)).toHaveCount(0);
        await expect(inspector.addConditionButton).toBeVisible();
        // No Switch rule card: no "Routing Rule n", Rename Output, Output Name or Fallback output.
        await expect(
          inspector.dialog.getByText(/Routing Rule|Rename Output|Output Name|Fallback output/),
        ).toHaveCount(0);
      });

      // ---- §10 step 2 ----------------------------------------------------------------

      await test.step("[H5] Configure E1 with AND (status equals paid; total Number greater than 100) and save", async () => {
        await inspector.valueToCheck(1).fill("{{ $json.status }}");
        await inspector.valueToCompare(1).fill("paid");
        await inspector.addConditionButton.click();
        await expect(inspector.valueToCheck(2)).toBeVisible();
        await inspector.valueToCheck(2).fill("{{ $json.total }}");
        await inspector.chooseTypeAndOperator(2, "Number", "Greater than");
        await inspector.valueToCompare(2).fill("100");
        await inspector.expectMatch("AND");

        await inspector.close();
        await editor.saveWorkflow();
      });

      await test.step("[H11] After a reload the inspector restores the conditions, operator labels and AND; the canvas keeps its handles", async () => {
        await page.reload({ waitUntil: "domcontentloaded" });
        await expect(editor.editorTab).toBeVisible({ timeout: 30_000 });

        const filterNode = editor.canvasNode(FILTER);
        await expect(filterNode).toBeVisible({ timeout: 30_000 });
        await expect(editor.targetHandles(filterNode)).toHaveCount(1);
        await expect(editor.sourceHandles(filterNode)).toHaveCount(1);
        const filterId = await editor.nodeId(FILTER);
        await expect(editor.edgeBetween(await editor.nodeId(SET_FIELD), filterId)).toHaveCount(1);
        await expect(editor.edgeBetween(filterId, await editor.nodeId(AFTER))).toHaveCount(1);

        await openInEditor(FILTER);
        await inspector.expectMatch("AND");
        await inspector.expectCondition(1, {
          left: "{{ $json.status }}",
          right: "paid",
          typeLabel: "String",
          operatorLabel: "Equals",
        });
        await inspector.expectCondition(2, {
          left: "{{ $json.total }}",
          right: "100",
          typeLabel: "Number",
          operatorLabel: "Greater than",
        });
        await expect(inspector.valueToCheck(3)).toHaveCount(0);
        await inspector.close();
      });

      await test.step("[H2/H5] Exported workflow JSON: filter/logic/v2, source + target handles, top-level conditionType and conditions, no filterConditions", async () => {
        const exported = await editor.exportWorkflow();
        const filterNode = nodeNamed(exported, FILTER);

        expect(filterNode.type).toBe("filter");
        expect(filterNode.category).toBe("logic");
        expect(filterNode.version).toBe("v2");
        expect(filterNode.handle?.targets?.map((entry) => entry.key)).toEqual(["target"]);
        expect(filterNode.handle?.sources?.map((entry) => entry.key)).toEqual(["source"]);

        expect(filterNode.parameters.conditionType).toBe("and");
        expect(filterNode.parameters.conditions).toEqual([
          { left: "{{ $json.status }}", operator: "equals", right: "paid", type: "string" },
          { left: "{{ $json.total }}", operator: "greater_than", right: "100", type: "number" },
        ]);
        expect(Object.keys(filterNode.parameters).sort()).toEqual(["conditionType", "conditions"]);
        expect(JSON.stringify(filterNode)).not.toContain("filterConditions");

        const setFieldNode = nodeNamed(exported, SET_FIELD);
        const afterNode = nodeNamed(exported, AFTER);
        expect(exported.edges).toEqual(
          expect.arrayContaining([
            expect.objectContaining({ source: setFieldNode.id, target: filterNode.id }),
            expect.objectContaining({ source: filterNode.id, target: afterNode.id }),
          ]),
        );
      });

      // ---- §10 step 3 ----------------------------------------------------------------

      await test.step('[H6/H8/H9] Test run with AND: Filter outputs item id 1, After runs once, log "Kept 1 of 3 item(s).", $node["Set Field"] resolves to 1', async () => {
        await testRun(mainWebhookUrl, 1);
        await executions.expectStatus("Completed");

        // H6: exactly the id-1 item, its Output unchanged.
        await openInExecution(FILTER);
        expect(await inspector.readOutputRows()).toEqual([E1_ITEMS[0]]);

        // "After" ran once, on the single kept item. H9: `{{ $node["Set Field"].json.output.id }}`
        // (see H9_EXPRESSION in support/filter-fixtures.ts for why `.json.output.id`) resolves to
        // the original item's id through Filter's parent link.
        await openInExecution(AFTER);
        const afterRows = (await inspector.readOutputRows()) as Record<string, unknown>[];
        expect(afterRows).toHaveLength(1);
        expect(String(afterRows[0].seen)).toBe("1");
        expect(String(afterRows[0].origin)).toBe("1");
        await inspector.close();

        // H8: one Info line at stage node.filter.applied with counts only.
        await executions.openLogs();
        const line = await executions.expectLogLine("Kept 1 of 3 item(s).", "node.filter.applied");
        // Counts only: no item values or condition text (the row's time stamp is not checked).
        await expect(line).not.toContainText(/paid|\{\{|\$json/);
        await executions.closeLogs();
      });

      // ---- §10 step 4 ----------------------------------------------------------------

      await test.step('[H7/H5] Switch to OR and re-run: all 3 items kept in input order, "Kept 3 of 3 item(s)."; OR saved as conditionType "or"', async () => {
        await backToEditor();
        await openInEditor(FILTER);
        await inspector.orButton.click();
        await inspector.expectMatch("OR");
        await inspector.close();

        await testRun(mainWebhookUrl, 2);
        await executions.expectStatus("Completed");

        await openInExecution(FILTER);
        expect(await inspector.readOutputRows()).toEqual(E1_ITEMS);
        await inspector.close();

        await executions.openLogs();
        await executions.expectLogLine("Kept 3 of 3 item(s).", "node.filter.applied");
        await executions.closeLogs();

        await backToEditor();
        const exported = await editor.exportWorkflow();
        const filterNode = nodeNamed(exported, FILTER);
        expect(filterNode.parameters.conditionType).toBe("or");
        expect(Object.keys(filterNode.parameters).sort()).toEqual(["conditionType", "conditions"]);
      });

      // ---- §10 step 5 ----------------------------------------------------------------

      await test.step("[C1/C5] status equals `Paid` (AND): Filter succeeds with 0 items and After is not started", async () => {
        await openInEditor(FILTER);
        await inspector.andButton.click();
        await inspector.expectMatch("AND");
        await inspector.removeCondition(2);
        await expect(inspector.valueToCheck(2)).toHaveCount(0);
        await inspector.valueToCompare(1).fill("Paid");
        await inspector.expectCondition(1, {
          left: "{{ $json.status }}",
          right: "Paid",
          typeLabel: "String",
          operatorLabel: "Equals",
        });
        await inspector.close();

        await testRun(mainWebhookUrl, 3);
        await executions.expectStatus("Completed");

        await openInExecution(FILTER);
        expect(await inspector.readOutputRows()).toEqual([]);
        await openInExecution(AFTER);
        expect(await inspector.readOutputRows()).toEqual([]);
        await inspector.close();

        await executions.openLogs();
        await executions.expectLogLine("Kept 0 of 3 item(s).", "node.filter.applied");
        await expect(executions.nodeLogRows(FILTER).first()).toBeVisible();
        await expect(executions.nodeLogRows(AFTER)).toHaveCount(0);
        await executions.closeLogs();
      });

      // ---- §10 step 6 ----------------------------------------------------------------

      await test.step("[C3/C4] Negative: an imported Filter with operator starts_with fails the step and the run with the If/Switch message", async () => {
        const startsWith = await importTracked(
          page,
          startsWithWorkflowFile(`E2E Filter starts_with ${stamp}`),
        );
        created.push(startsWith);
        await expect(editor.canvasNode(FILTER)).toBeVisible();

        const webhookUrl = await readWebhookTestUrl();
        await testRun(webhookUrl, 1);
        await executions.expectStatus("Failed");

        await openInExecution(FILTER);
        await expect(inspector.failedRunAlert).toContainText(STARTS_WITH_FAILURE);
        // E4: every item became an error item; evaluation went on after item 0.
        const rows = (await inspector.readOutputRows()) as Record<string, unknown>[];
        expect(rows).toHaveLength(3);
        for (const row of rows) {
          expect(row).toMatchObject({
            error: true,
            message: "Unknown operator starts_with for type string",
          });
        }
        await inspector.close();

        // C4: the counts line is still written, and error items are not counted as kept.
        await executions.openLogs();
        await executions.expectLogLine("Kept 0 of 3 item(s).", "node.filter.applied");
        await executions.closeLogs();
      });

      // ---- §10 step 7 ----------------------------------------------------------------

      await test.step("[C6] Number literal `abc`: the invalid-literal hint shows; the run succeeds and keeps 0", async () => {
        await page.goto(main.url, { waitUntil: "domcontentloaded" });
        await expect(editor.editorTab).toBeVisible({ timeout: 30_000 });
        await expect(editor.canvasNode(FILTER)).toBeVisible({ timeout: 30_000 });

        await openInEditor(FILTER);
        await inspector.valueToCheck(1).fill("{{ $json.total }}");
        await inspector.chooseTypeAndOperator(1, "Number", "Greater than");
        await inspector.valueToCompare(1).fill("abc");
        await expect(
          inspector.hint("Not a number, so this condition never matches."),
        ).toBeVisible();
        await inspector.close();

        await testRun(mainWebhookUrl, 4);
        await executions.expectStatus("Completed");

        await openInExecution(FILTER);
        expect(await inspector.readOutputRows()).toEqual([]);
        await inspector.close();

        await executions.openLogs();
        await executions.expectLogLine("Kept 0 of 3 item(s).", "node.filter.applied");
        await executions.closeLogs();
      });

      // ---- §10 step 8 ----------------------------------------------------------------

      await test.step("[C11] A second Filter gets its own parameters: editing it leaves the first Filter unchanged", async () => {
        await backToEditor();
        await editor.openLibraryFromControls();
        await editor.libraryEntry("Filter").click();
        await expect(editor.nodeLibrarySheet).toBeHidden();
        await expect(editor.canvasNode(FILTER_2)).toHaveCount(1);

        await openInEditor(FILTER_2);
        await inspector.expectMatch("AND");
        await inspector.expectCondition(1, {
          left: "",
          right: "",
          typeLabel: "String",
          operatorLabel: "Equals",
        });
        await expect(inspector.valueToCheck(2)).toHaveCount(0);

        await inspector.valueToCheck(1).fill("{{ $json.id }}");
        await inspector.valueToCompare(1).fill("2");
        await inspector.orButton.click();
        await inspector.expectMatch("OR");

        await openInEditor(FILTER);
        await inspector.expectMatch("AND");
        await inspector.expectCondition(1, {
          left: "{{ $json.total }}",
          right: "abc",
          typeLabel: "Number",
          operatorLabel: "Greater than",
        });
        await expect(inspector.valueToCheck(2)).toHaveCount(0);

        await openInEditor(FILTER_2);
        await inspector.expectMatch("OR");
        await inspector.expectCondition(1, {
          left: "{{ $json.id }}",
          right: "2",
          typeLabel: "String",
          operatorLabel: "Equals",
        });
        await inspector.close();
      });

      // ---- §10 step 9 ----------------------------------------------------------------

      await test.step("[C12] Execution view of the step-3 run: AND/OR, picker, add, remove and inputs are disabled", async () => {
        await executions.open();
        await executions.waitForRunCount(4);
        // Newest first: the step-3 run (the first one) is the last row.
        const step3Run = executions.runRows.last();
        await executions.waitForFinished(step3Run);
        await executions.openRun(step3Run);
        await executions.expectStatus("Completed");

        await openInExecution(FILTER);
        // The run's snapshot holds the E1 conditions.
        await expect(inspector.valueToCompare(2)).toHaveValue("100");
        await expect(inspector.andButton).toBeDisabled();
        await expect(inspector.orButton).toBeDisabled();
        for (const n of [1, 2]) {
          await expect(inspector.typeAndOperatorPicker(n)).toBeDisabled();
          await expect(inspector.valueToCheck(n)).toBeDisabled();
          await expect(inspector.valueToCompare(n)).toBeDisabled();
          await expect(inspector.removeConditionButton(n)).toBeDisabled();
        }
        await expect(inspector.addConditionButton).toBeDisabled();
        await inspector.close();
      });

      // ---- §10 step 10 ---------------------------------------------------------------

      await test.step("[H12] The Filter guide opens from the inspector", async () => {
        await backToEditor();
        await openInEditor(FILTER);
        await inspector.guideButton.click();
        await expect(inspector.guidePanelHeading).toBeVisible();
        await expect(
          inspector.dialog.getByRole("heading", { level: 4, name: "Filter" }),
        ).toBeVisible();
        await expect(
          inspector.dialog.getByText(
            "Use this node to keep only the items that match the conditions. The other items are removed.",
          ),
        ).toBeVisible();
        await inspector.hideGuideButton.click();
        await expect(inspector.guidePanelHeading).toBeHidden();
        await inspector.close();
      });

      // ---- §10 step 11 ---------------------------------------------------------------

      await test.step("[C2] Join: Filter (removes every item) and a second branch feed Merge with Run When = Any; the run completes and Merge runs", async () => {
        const join = await importTracked(page, joinWorkflowFile(`E2E Filter join ${stamp}`));
        created.push(join);
        await expect(editor.canvasNode(MERGE)).toBeVisible();

        const webhookUrl = await readWebhookTestUrl();
        await testRun(webhookUrl, 1);
        await executions.expectStatus("Completed");

        await openInExecution(FILTER);
        expect(await inspector.readOutputRows()).toEqual([]);
        // Merge was started (the join wake, since Filter sent it nothing) and output Input 2's items.
        await openInExecution(MERGE);
        expect(await inspector.readOutputRows()).toEqual(E1_ITEMS);
        await inspector.close();

        await executions.openLogs();
        await executions.expectLogLine("Kept 0 of 3 item(s).", "node.filter.applied");
        await expect(executions.nodeLogRows(MERGE).first()).toBeVisible();
        await executions.closeLogs();
      });
    } catch (error) {
      primaryFailure = true;
      throw error;
    } finally {
      await test.step("[Cleanup] Delete every workflow this spec created", async () => {
        const problems: string[] = [];
        for (const workflow of created) {
          try {
            await deleteWorkflowAt(page, workflow);
          } catch (error) {
            problems.push(`${workflow.name} (${workflow.url}): ${(error as Error).message}`);
          }
        }
        for (const name of importedNames.filter((n) => !created.some((w) => w.name === n))) {
          try {
            await deleteWorkflowNamed(page, name);
          } catch (error) {
            problems.push(`${name}: ${(error as Error).message}`);
          }
        }
        if (problems.length === 0) return;
        const message = `Could not delete ${problems.length} test workflow(s):\n${problems.join("\n")}`;
        // Do not hide the step that failed first behind a cleanup error.
        if (primaryFailure) console.warn(message);
        else throw new Error(message);
      });
    }
  });
});

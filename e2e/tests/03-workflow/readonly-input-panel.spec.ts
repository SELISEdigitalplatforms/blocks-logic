import { test, expect } from "../../support/test-base";
import type { ImportEdge, ImportNode, WorkflowImportFile } from "../../support/filter-fixtures";
import {
  deleteWorkflowAt,
  deleteWorkflowNamed,
  importWorkflowFile,
  type ImportedWorkflow,
} from "../../support/workflow-import";
import {
  ClipboardComponent,
  NodeInspectorPage,
  WorkflowEditorPage,
  WorkflowExecutionsPage,
} from "../../po";
import type { Locator, Page } from "@playwright/test";

/**
 * Read-only Input panel — spec blocks-logic-415 §10 steps 1–6 (Q1: B).
 *
 * In the execution and version views the node inspector's Input panel is ReadonlyInputPanel
 * (shared/readonly-input-panel): the node's own recorded input, laid out like the Output panel, with
 * nothing draggable and no mock-data controls. The workflow editor keeps the draggable InputPanel.
 *
 * Fixture (§10 "RO-Input-E2E"), imported through the workflow list's Import control:
 *   Webhook → Code "Shape" (returns [{ name: "Ada", age: 36, tags: ["x"] }]) → Code "Echo" (returns
 *   its input unchanged), plus Code "Unused", not connected, so it never runs (C3: no recorded run).
 * The failed-node case (§10 step 5) imports a second copy whose Echo throws, instead of editing and
 * reverting Echo's script in the code editor. Both workflows are deleted in the `finally` block.
 *
 * §10 steps 7 (network, C9) and 8 (screenshots, H9) are captured outside the suite (D6).
 *
 * Runs in the shared suite project ("test"): openWorkflowList() opens that project's list.
 */

const NAMES = { webhook: "Webhook", shape: "Shape", echo: "Echo", unused: "Unused" } as const;

const SHAPE_ROWS = [{ name: "Ada", age: 36, tags: ["x"] }];
const WEBHOOK_BODY = [{ seed: 1 }];

const singleHandle = () => ({
  sources: [{ key: "source", label: "" }],
  targets: [{ key: "target", label: "" }],
});

/** Same Webhook settings as the Filter spec: "last" runs the whole workflow in the Api that answers. */
const webhookNode = (id: string): ImportNode => ({
  id,
  name: NAMES.webhook,
  category: "trigger",
  type: "webhook",
  version: "v2",
  position: { x: 0, y: 0 },
  handle: { sources: [{ key: "source", label: "" }], targets: [] },
  parameters: {
    httpMethod: "POST",
    authType: "none",
    httpResponseMode: "last",
    httpResponseData: "all",
    organizationId: "",
    roles: { operator: "all", values: [] },
    permissions: { operator: "any", values: [] },
    authorizationMode: "RolesOnly",
    path: id,
  },
  settings: {},
  pinData: null,
});

/** Code node (transform / code / v1), "Run once for all items", JavaScript. */
const codeNode = (id: string, name: string, x: number, y: number, script: string): ImportNode => ({
  id,
  name,
  category: "transform",
  type: "code",
  version: "v1",
  position: { x, y },
  handle: singleHandle(),
  parameters: { mode: "all", language: "js", script },
  settings: {},
  pinData: null,
});

const edge = (source: string, target: string): ImportEdge => ({
  id: `xy-edge__${source}-${target}`,
  source,
  target,
  sourceHandle: "source",
  targetHandle: "target",
});

const ECHO_SCRIPT = "return $items.map((item) => item.json);";
const FAILING_ECHO_SCRIPT = 'throw new Error("boom");';

function roInputWorkflowFile(name: string, echoScript: string): WorkflowImportFile {
  const ids = {
    webhook: "e2eroinputwebhook00000000000001",
    shape: "e2eroinputshape0000000000000001",
    echo: "e2eroinputecho00000000000000001",
    unused: "e2eroinputunused000000000000001",
  };
  return {
    name,
    settings: {},
    nodes: [
      webhookNode(ids.webhook),
      codeNode(ids.shape, NAMES.shape, 300, 0, `return ${JSON.stringify(SHAPE_ROWS)};`),
      codeNode(ids.echo, NAMES.echo, 600, 0, echoScript),
      codeNode(ids.unused, NAMES.unused, 300, 250, "return [];"),
    ],
    edges: [edge(ids.webhook, ids.shape), edge(ids.shape, ids.echo)],
  };
}

/** A panel of the open inspector by its h3 title ("Input" / "Output"): the title's panel root. */
function ioPanel(inspector: NodeInspectorPage, title: "Input" | "Output"): Locator {
  return inspector.dialog
    .getByRole("heading", { level: 3, name: title, exact: true })
    .locator("xpath=../../..");
}

/** The mock-data controls and badge of the Output panel, none of which may be in the Input panel. */
async function expectNoMockDataControls(panel: Locator): Promise<void> {
  for (const name of ["Set mock data", "Edit mock data", "Remove mock data"]) {
    await expect(panel.getByRole("button", { name })).toHaveCount(0);
  }
  await expect(panel.getByText("Mock data", { exact: true })).toHaveCount(0);
  await expect(panel.locator("textarea")).toHaveCount(0);
}

async function expectNothingDraggable(panel: Locator): Promise<void> {
  await expect(panel.locator('[draggable="true"]')).toHaveCount(0);
  await expect(panel.locator("[title^='Drag to use']")).toHaveCount(0);
}

/** One schema row of an Input/Output panel card: `key:` followed by its value. */
function schemaRow(panel: Locator, key: string): Locator {
  return panel.locator("div.group").filter({ has: panel.page().getByText(`${key}:`, { exact: true }) });
}

test.describe("workflow — read-only Input panel", () => {
  test("Input panel: draggable in the editor, read-only in the execution and version views", async ({
    page,
  }) => {
    test.setTimeout(20 * 60_000);

    const editor = new WorkflowEditorPage(page);
    const inspector = new NodeInspectorPage(page);
    const executions = new WorkflowExecutionsPage(page);
    const clipboard = new ClipboardComponent(page);

    const stamp = Date.now();
    const created: ImportedWorkflow[] = [];
    const importedNames: string[] = [];
    let primaryFailure = false;

    const importTracked = async (file: WorkflowImportFile) => {
      importedNames.push(file.name);
      const imported = await importWorkflowFile(page, file);
      created.push(imported);
      return imported;
    };

    const openInEditor = async (name: string) => {
      await inspector.closeIfOpen();
      await editor.clickCanvasNode(name);
      await inspector.expectOpenFor(name);
    };

    const openInExecution = async (name: string) => {
      await executions.closeLogs();
      await inspector.closeIfOpen();
      await executions.openNode(name);
      await inspector.expectOpenFor(name);
    };

    const readWebhookTestUrl = async (): Promise<string> => {
      await inspector.closeIfOpen();
      await editor.clickCanvasNode(NAMES.webhook);
      await expect(inspector.dialog).toBeVisible();
      const url = await inspector.deprecatedWebhookUrlInput.inputValue();
      expect(url, "webhook test URL with tenant, workflow and node in the path").toMatch(
        /\/Workflow\/webhook-test\/[^/]+\/[^/]+\/[^/]+$/,
      );
      await inspector.close();
      return url;
    };

    /** Execute Workflow, POST the body to the webhook test URL, and open the finished run. */
    const testRun = async (webhookUrl: string, expectedRunCount: number) => {
      await inspector.closeIfOpen();
      await editor.startTestListener();
      const response = await page.request.post(webhookUrl, { data: WEBHOOK_BODY });
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
      const main = await test.step("[Setup] Import RO-Input-E2E: Webhook → Shape → Echo, and Unused", async () => {
        const imported = await importTracked(roInputWorkflowFile(`RO-Input-E2E ${stamp}`, ECHO_SCRIPT));
        for (const name of Object.values(NAMES)) await expect(editor.canvasNode(name)).toBeVisible();
        return imported;
      });

      const webhookUrl = await test.step("[Setup] Read the Webhook test URL", readWebhookTestUrl);

      await test.step("[Setup] Run the workflow once and open the run (§10 step 2)", async () => {
        await testRun(webhookUrl, 1);
        await executions.expectStatus("Completed");
      });

      // ---- §10 step 2: execution view --------------------------------------------------

      await test.step("[H1/H4/H5] Execution view, Echo: the Input panel shows Echo's own input as item cards", async () => {
        await openInExecution(NAMES.echo);
        const input = ioPanel(inspector, "Input");
        await expect(input).toBeVisible();
        await expect(ioPanel(inspector, "Output")).toBeVisible();

        await expect(input.getByText("item 1:", { exact: true })).toBeVisible();
        await expect(input.getByText("item 2:", { exact: true })).toHaveCount(0);
        await expect(schemaRow(input, "name")).toContainText("Ada");
        await expect(schemaRow(input, "age")).toContainText("36");
        // C8: a nested value is single-line JSON.
        await expect(schemaRow(input, "tags")).toContainText('["x"]');
        await expect(input.getByRole("button", { name: "Copy value of name" })).toHaveCount(1);
      });

      await test.step("[C1/C2/C6] Execution view: nothing draggable, no mock-data controls, no predecessor selector or summaries", async () => {
        const input = ioPanel(inspector, "Input");
        await expectNothingDraggable(input);
        // Trying to drag a field starts nothing: it is plain text, not a draggable element.
        await expect(input.getByText("name:", { exact: true })).not.toHaveAttribute("draggable", "true");
        await expectNoMockDataControls(input);
        await expect(input.getByRole("combobox")).toHaveCount(0);
        await expect(input.getByText(/ in → .* out$/)).toHaveCount(0);
      });

      await test.step("[H6] Execution view, Table tab: one column per key", async () => {
        const input = ioPanel(inspector, "Input");
        await input.getByRole("tab", { name: "Table", exact: true }).click();
        const headers = input.getByRole("columnheader");
        await expect(headers).toHaveText(["name", "age", "tags"]);
        await expect(input.getByRole("cell")).toHaveText(["Ada", "36", '["x"]']);
        await expectNothingDraggable(input);
      });

      await test.step("[H7] Execution view, JSON tab: pretty JSON, and Copy input JSON copies exactly that text", async () => {
        const input = ioPanel(inspector, "Input");
        await input.getByRole("tab", { name: "JSON", exact: true }).click();
        const expected = JSON.stringify(SHAPE_ROWS, null, 2);
        expect(await input.locator("pre code").textContent()).toBe(expected);

        await clipboard.grantPermissions();
        await clipboard.writeText("");
        await input.getByRole("button", { name: "Copy input JSON" }).click();
        await expect.poll(() => clipboard.readText()).toBe(expected);
        await expectNothingDraggable(input);
        await input.getByRole("tab", { name: "Schema", exact: true }).click();
      });

      // ---- §10 step 3: collapse ----------------------------------------------------------

      await test.step("[H8/C10] Collapse input hides its body and keeps the Output panel; Expand input restores it", async () => {
        const input = ioPanel(inspector, "Input");
        const output = ioPanel(inspector, "Output");
        await inspector.dialog.getByRole("button", { name: "Collapse input" }).click();
        await expect(input.getByText("item 1:", { exact: true })).toHaveCount(0);
        await expect(inspector.dialog.getByRole("button", { name: "Expand input" })).toBeVisible();
        await expect(output.getByRole("button", { name: "Collapse output" })).toBeVisible();

        await inspector.dialog.getByRole("button", { name: "Expand input" }).click();
        await expect(input.getByText("item 1:", { exact: true })).toBeVisible();

        // C10: the page object's readOutputRows still finds "Collapse input" and reads the Output JSON.
        expect(await inspector.readOutputRows()).toEqual(SHAPE_ROWS);
      });

      await test.step("[H12] Execution view: selecting Shape shows Shape's own input (the webhook item)", async () => {
        await openInExecution(NAMES.shape);
        const input = ioPanel(inspector, "Input");
        await expect(schemaRow(input, "seed")).toContainText("1");
        await expect(schemaRow(input, "name")).toHaveCount(0);
      });

      // ---- §10 step 4: no input ----------------------------------------------------------

      await test.step("[C3] Execution view: a node with no recorded run shows the empty texts on every tab", async () => {
        await openInExecution(NAMES.unused);
        const input = ioPanel(inspector, "Input");
        await expect(input.getByText("No runtime input schema available.", { exact: true })).toBeVisible();
        await input.getByRole("tab", { name: "Table", exact: true }).click();
        await expect(input.getByText("No runtime input data available.", { exact: true })).toBeVisible();
        await input.getByRole("tab", { name: "JSON", exact: true }).click();
        await expect(input.getByText("No runtime input data available.", { exact: true })).toBeVisible();
        await expect(input.locator("pre code")).toHaveCount(0);
        await inspector.close();
      });

      // ---- §10 step 6: version view ------------------------------------------------------

      await test.step("[H2/C1/C2] Version view: publish a version, open it, and select Echo", async () => {
        await editor.editorTab.click();
        await expect(editor.editorTab).toHaveAttribute("data-state", "active");
        await expect(editor.publishTrigger).toBeEnabled({ timeout: 15_000 });
        await editor.publishTrigger.click();
        const versionName = page.locator("#version-name");
        await expect(versionName).toBeVisible();
        const name = `RO-Input ${stamp}`;
        await versionName.fill(name);
        await page.getByRole("dialog").getByRole("button", { name: "Publish", exact: true }).click();
        await expect(versionName).toBeHidden({ timeout: 30_000 });

        await editor.versionsTab.click();
        await expect(editor.versionsTab).toHaveAttribute("data-state", "active");
        const versions = page.getByRole("tabpanel", { name: "Versions" });
        await versions.getByText(name, { exact: true }).click();
        await editor.clickCanvasNode(NAMES.echo, versions);
        await inspector.expectOpenFor(NAMES.echo);

        const input = ioPanel(inspector, "Input");
        await expect(input).toBeVisible();
        await expectNothingDraggable(input);
        await expectNoMockDataControls(input);
        await expect(input.getByRole("combobox")).toHaveCount(0);
        await inspector.close();
      });

      // ---- §10 step 1: editor --------------------------------------------------------------

      await test.step("[H3/H10] Editor, Echo: the Input panel is the draggable InputPanel", async () => {
        await page.goto(main.url, { waitUntil: "domcontentloaded" });
        await expect(editor.editorTab).toBeVisible({ timeout: 30_000 });
        await editor.editorTab.click();
        await openInEditor(NAMES.echo);
        const input = ioPanel(inspector, "Input");
        // The field comes from Echo's parent Shape (the last run or the step run), and is draggable.
        const nameField = input.locator('[draggable="true"]').filter({ hasText: /^\s*name:?\s*$/ });
        await expect(nameField.first()).toBeVisible({ timeout: 30_000 });
        await expect(nameField.first()).toHaveAttribute("title", /^Drag to use: .*name/);
        await inspector.close();
      });

      // ---- §10 step 5: failed node -------------------------------------------------------

      await test.step("[C5] Failed Echo: the Input panel shows Echo's input; the error alert is only in the Output panel", async () => {
        await importTracked(roInputWorkflowFile(`RO-Input-E2E fail ${stamp}`, FAILING_ECHO_SCRIPT));
        const failUrl = await readWebhookTestUrl();
        await inspector.closeIfOpen();
        await editor.startTestListener();
        // The run fails at Echo; the webhook may answer with an error status, so only the run is checked.
        await page.request.post(failUrl, { data: WEBHOOK_BODY });
        await executions.open();
        await executions.waitForRunCount(1);
        const latest = executions.runRows.first();
        await executions.waitForFinished(latest);
        await executions.openRun(latest);
        await executions.expectStatus("Failed");

        await openInExecution(NAMES.echo);
        const input = ioPanel(inspector, "Input");
        const output = ioPanel(inspector, "Output");
        await expect(schemaRow(input, "name")).toContainText("Ada");
        await expect(input.getByRole("alert")).toHaveCount(0);
        await expect(output.getByRole("alert")).toContainText("boom");
        await inspector.close();
      });
    } catch (error) {
      primaryFailure = true;
      throw error;
    } finally {
      await test.step("[Cleanup] Delete every workflow this spec created", async () => {
        await cleanup(page, created, importedNames, primaryFailure);
      });
    }
  });
});

async function cleanup(
  page: Page,
  created: ImportedWorkflow[],
  importedNames: string[],
  primaryFailure: boolean,
): Promise<void> {
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
}

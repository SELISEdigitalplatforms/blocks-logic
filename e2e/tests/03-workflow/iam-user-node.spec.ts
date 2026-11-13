import { test, expect } from "../../support/test-base";
import { IAM_NODE_NAMES, iamUserWorkflowFile } from "../../support/iam-user-fixtures";
import {
  deleteWorkflowAt,
  deleteWorkflowNamed,
  importWorkflowFile,
  type ImportedWorkflow,
} from "../../support/workflow-import";
import { NodeInspectorPage, WorkflowEditorPage } from "../../po";

/**
 * IAM User node on dev — blocks-logic#424 §10 (phase 2 of the IAM User node; the Fields block is #423).
 *
 * The fixture (support/iam-user-fixtures.ts) holds three IAM User nodes that are not wired to the trigger,
 * so the inspector's "Execute Step" runs each once on its own as the signed-in editor user:
 * Each step's checks are soft, so every criterion is reported even when an earlier one fails.
 * - W10 / H13: Blocks Authentication + Get Users (page size 1) succeeds and returns users;
 * - W11: page size 101 fails the item with `validation_failed`;
 * - §6 clientId rule: Client Credential with no credential fails the item with `invalid_configuration`.
 * Only reads are sent to IAM. The node's base URL is left blank, so the run uses the server's `IamBaseUrl`
 * setting, the ticket's operations prerequisite.
 *
 * The live steps that need dedicated IAM fixtures (client credentials with chosen permissions, the e2e
 * organization and roles, user creation, role changes, sessions, logs) are not automated here.
 */

type OutputRow = Record<string, unknown>;

test.describe("workflow — IAM User node (blocks-logic#424)", () => {
  test("§10 W10, W11 and the client credential rule through Execute Step", async ({
    page,
  }, testInfo) => {
    test.setTimeout(8 * 60_000);

    const editor = new WorkflowEditorPage(page);
    const inspector = new NodeInspectorPage(page);
    const name = `E2E IAM User ${Date.now()}`;
    let imported: ImportedWorkflow | undefined;
    let primaryFailure = false;

    /** Open `node`, press Execute Step, wait for the run and return its output rows. */
    const executeStep = async (node: string): Promise<OutputRow[]> => {
      await inspector.closeIfOpen();
      await editor.clickCanvasNode(node);
      await inspector.expectOpenFor(node);
      const stepRun = page.waitForResponse(
        (response) =>
          /\/Workflow\/StepExecute/i.test(response.url()) && response.request().method() === "POST",
        { timeout: 90_000 },
      );
      await inspector.dialog.getByRole("button", { name: "Execute Step" }).click();
      const response = await stepRun;
      expect(response.ok(), `StepExecute answered HTTP ${response.status()}`).toBeTruthy();
      let rows: OutputRow[] = [];
      await expect
        .poll(
          async () => {
            rows = (await inspector.readOutputRows()) as OutputRow[];
            return rows.length;
          },
          { timeout: 90_000, message: `${node} shows its output` },
        )
        .toBeGreaterThan(0);
      // Keys and error fields only: a user row carries personal data (email, names).
      await testInfo.attach(`${node}-output.json`, {
        body: JSON.stringify(
          rows.map((row) => ({
            keys: Object.keys(row).sort(),
            error: row.error,
            code: row.code,
            message: row.message,
          })),
          null,
          2,
        ),
        contentType: "application/json",
      });
      return rows;
    };

    try {
      imported = await test.step("[Setup] Import three standalone IAM User nodes", async () => {
        const workflow = await importWorkflowFile(page, iamUserWorkflowFile(name));
        for (const node of Object.values(IAM_NODE_NAMES))
          await expect(editor.canvasNode(node)).toBeVisible();
        return workflow;
      });

      await test.step("§6 clientId: Client Credential with no credential fails with invalid_configuration", async () => {
        const rows = await executeStep(IAM_NODE_NAMES.noCredential);
        expect(rows).toHaveLength(1);
        expect(rows[0]).toMatchObject({
          error: true,
          code: "invalid_configuration",
          message: "Select a client credential for this node.",
        });
      });
      await test.step("W10 / H13: Blocks Authentication, Get Users, page size 1 returns users", async () => {
        const rows = await executeStep(IAM_NODE_NAMES.getUsers);
        const failure = rows.find((row) => row.error === true);
        expect.soft(failure, `no error item (got ${JSON.stringify(failure)})`).toBeUndefined();
        expect.soft(rows, "page size 1 returns one user").toHaveLength(1);
        expect.soft(rows[0]).toHaveProperty("_totalCount");
        expect
          .soft(Number(rows[0]._totalCount), "the project has at least one user")
          .toBeGreaterThan(0);
      });

      await test.step("W11: page size 101 fails the item with validation_failed", async () => {
        const rows = await executeStep(IAM_NODE_NAMES.pageSize101);
        expect.soft(rows).toHaveLength(1);
        expect
          .soft(rows[0])
          .toMatchObject({ error: true, code: "validation_failed", operation: "getUsers" });
        expect.soft(rows[0].message).toBe("Page size must be between 1 and 100.");
      });
    } catch (error) {
      primaryFailure = true;
      throw error;
    } finally {
      await test.step("[Cleanup] Delete the fixtures this spec created", async () => {
        try {
          await inspector.closeIfOpen();
          if (imported) await deleteWorkflowAt(page, imported);
          else await deleteWorkflowNamed(page, name);
        } catch (cleanupError) {
          if (!primaryFailure) throw cleanupError;
          console.warn(`[cleanup] could not delete "${name}": ${String(cleanupError)}`);
        }
      });
    }
  });
});

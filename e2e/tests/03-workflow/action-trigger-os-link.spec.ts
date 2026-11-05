import type { Request } from "@playwright/test";
import { test, expect } from "../../support/test-base";
import { e2eBaseUrl, e2eOsBaseUrl } from "../../support/env";
import type { WorkflowImportFile } from "../../support/filter-fixtures";
import {
  deleteWorkflowAt,
  deleteWorkflowNamed,
  importWorkflowFile,
  type ImportedWorkflow,
} from "../../support/workflow-import";
import { NodeInspectorPage, WorkflowEditorPage } from "../../po";

/**
 * Action trigger → "Configure actions in Blocks OS" link — spec blocks-logic SPEC12 §10.
 *
 * Imports a workflow holding only an Action trigger (type `blocksEvent` v2, no action chosen), checks the
 * inspector (no Notes callout, the link and its attributes), follows the link into a new tab and expects it to
 * land signed in on OS `/app/<projectId>/secret-management/actions`. Negative cases: a non-GUID project id and
 * an IAM failure (route-mocked). The workflow is deleted in `finally`. OS action settings are never changed.
 */

const ACTION = "Action";
const LINK_NAME = "Configure actions in Blocks OS (opens in a new tab)";
const INITIATE = "**/api/idp/initiate**";

const actionWorkflowFile = (name: string): WorkflowImportFile => ({
  name,
  settings: {},
  nodes: [
    {
      id: "actiontrigger1",
      name: ACTION,
      category: "trigger",
      type: "blocksEvent",
      version: "v2",
      position: { x: 0, y: 0 },
      handle: { sources: [{ key: "source", label: "" }], targets: [] },
      parameters: { eventName: "" },
      settings: {},
      pinData: null,
    },
  ],
  edges: [],
});

const isInitiate = (request: Request) => request.url().includes("/api/idp/initiate");

test.describe("workflow — Action trigger OS link", () => {
  test("opens this project's OS Actions page in a new tab, signed in, leaving the editor as it was", async ({
    page,
    context,
  }) => {
    test.setTimeout(5 * 60_000);
    const editor = new WorkflowEditorPage(page);
    const inspector = new NodeInspectorPage(page);
    const name = `e2e-action-os-link-${Date.now()}`;
    let workflow: ImportedWorkflow | undefined;

    try {
      workflow = await importWorkflowFile(page, actionWorkflowFile(name));
      const projectId = new URL(workflow.url).pathname.split("/")[2];
      const osActions = new RegExp(
        `^${e2eOsBaseUrl().replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}/app/${projectId}/secret-management/actions`,
      );

      // Step 1 — H1–H5, H12.
      const editorInitiates: Request[] = [];
      page.on("request", (r) => isInitiate(r) && editorInitiates.push(r));
      await editor.clickCanvasNode(ACTION);
      await inspector.expectOpenFor(ACTION);
      await expect(inspector.dialog.getByText("Notes", { exact: true })).toHaveCount(0);
      await expect(inspector.dialog.getByText(/Runs the .*published.* workflow only/)).toHaveCount(0);
      const link = inspector.dialog.getByRole("link", { name: LINK_NAME });
      await expect(link).toBeVisible();
      await expect(link).toHaveAttribute("href", `/app/${projectId}/open-in-os/actions`);
      await expect(link).toHaveAttribute("target", "_blank");
      await expect(link).toHaveAttribute("rel", "noopener noreferrer");
      expect(editorInitiates).toHaveLength(0);

      // Steps 2–3 — H6, H7, H8, C9.
      const editorUrl = page.url();
      const [osTab] = await Promise.all([context.waitForEvent("page"), link.click()]);
      const tabInitiates: Request[] = [];
      osTab.on("request", (r) => isInitiate(r) && tabInitiates.push(r));
      await expect(osTab).toHaveURL(osActions, { timeout: 90_000 });
      expect(tabInitiates.length).toBeLessThanOrEqual(1);
      if (tabInitiates[0]) {
        expect(new URL(tabInitiates[0].url()).searchParams.get("forwardedTo")).toBe(
          `/app/${projectId}/secret-management/actions`,
        );
      }
      expect(page.url()).toBe(editorUrl);
      await inspector.expectOpenFor(ACTION);

      // Step 4 — C8: a second click lands on its own.
      const [secondTab] = await Promise.all([context.waitForEvent("page"), link.click()]);
      await expect(secondTab).toHaveURL(osActions, { timeout: 90_000 });
      await osTab.close();
      await secondTab.close();

      // Step 5 — C4: a non-GUID project id never asks IAM.
      const badTab = await context.newPage();
      const badInitiates: Request[] = [];
      badTab.on("request", (r) => isInitiate(r) && badInitiates.push(r));
      await badTab.goto(`${e2eBaseUrl()}/app/not-a-guid/open-in-os/actions`, { waitUntil: "domcontentloaded" });
      await expect(badTab.getByRole("alert")).toHaveText("Could not open Blocks OS.", { timeout: 30_000 });
      await expect(badTab.getByRole("button", { name: "Try again" })).toHaveCount(0);
      expect(badInitiates).toHaveLength(0);
      await badTab.close();

      // Step 6 — C6, C7: IAM fails, then Try again asks once more and lands.
      const failTab = await context.newPage();
      await failTab.route(INITIATE, (route) =>
        route.fulfill({ status: 400, contentType: "application/json", body: '{"error":"invalid_redirect_uri"}' }),
      );
      await failTab.goto(`${e2eBaseUrl()}/app/${projectId}/open-in-os/actions`, { waitUntil: "domcontentloaded" });
      await expect(failTab.getByRole("alert")).toHaveText("Could not open Blocks OS.", { timeout: 30_000 });
      await failTab.unroute(INITIATE);
      await failTab.getByRole("button", { name: "Try again" }).click();
      await expect(failTab).toHaveURL(osActions, { timeout: 90_000 });
      await failTab.close();
    } finally {
      if (workflow) await deleteWorkflowAt(page, workflow);
      else await deleteWorkflowNamed(page, name).catch(() => undefined);
    }
  });
});

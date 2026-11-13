import type { Page, Route } from "@playwright/test";
import { test, expect } from "../../support/test-base";
import { CODE_NODE_NAMES, twoCodeNodesWorkflowFile } from "../../support/code-node-fixtures";
import {
  createFunctionInUi,
  deleteFunctionInUi,
  openInFreshDocument,
  type CreatedFunction,
} from "../../support/function-ui";
import { readLogicProject } from "../../support/logic-project";
import {
  deleteWorkflowAt,
  deleteWorkflowNamed,
  importWorkflowFile,
  type ImportedWorkflow,
} from "../../support/workflow-import";
import { NodeInspectorPage, TopbarComponent, WorkflowEditorPage } from "../../po";

/**
 * Safe Monaco loader — blocks-logic#435 §10 steps 1–6 on dev.
 *
 * The spec's manual run uses DevTools (Slow 3G, request blocking, the Network count). Here:
 * - "before the editors appear" is made certain by holding every `/monaco/vs/*` response for a few seconds
 *   (the loader's own requests) with the browser cache disabled, instead of throttling the whole app;
 * - unhandled rejections are recorded in the page (`unhandledrejection`) and through `pageerror`;
 * - step 4 counts `/monaco/vs` requests; step 5 aborts `loader.js`.
 * Fixtures: one function created for the test through "New function" (never run or deployed) and a workflow with
 * two Code nodes; both are deleted at the end.
 */

const MONACO_VS = /\/monaco\/vs\//;
const HOLD_MS = 6_000;

type Watch = { rejections: string[]; pageErrors: string[]; consoleErrors: string[] };

/** Records unhandled rejections (reason type/message), page errors and console errors from now on. */
async function watchPage(page: Page): Promise<Watch> {
  const watch: Watch = { rejections: [], pageErrors: [], consoleErrors: [] };
  await page
    .exposeFunction("__e2eRejection", (text: string) => watch.rejections.push(text))
    .catch(() => undefined);
  await page.addInitScript(() => {
    window.addEventListener("unhandledrejection", (event) => {
      const reason = event.reason as
        { type?: unknown; msg?: unknown; message?: unknown } | undefined;
      const text = JSON.stringify({
        type: reason?.type,
        msg: reason?.msg,
        message: reason?.message ?? String(reason),
      });
      (window as unknown as { __e2eRejection?: (t: string) => void }).__e2eRejection?.(text);
    });
  });
  page.on("pageerror", (error) => watch.pageErrors.push(error.message));
  page.on("console", (message) => {
    if (message.type() === "error") watch.consoleErrors.push(message.text());
  });
  return watch;
}

const reset = (watch: Watch) => {
  watch.rejections.length = 0;
  watch.pageErrors.length = 0;
  watch.consoleErrors.length = 0;
};

/** Hold every Monaco loader response for HOLD_MS (with the cache off), so editors are still loading. */
async function holdMonaco(page: Page): Promise<() => Promise<void>> {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send("Network.enable");
  await cdp.send("Network.setCacheDisabled", { cacheDisabled: true });
  const handler = async (route: Route) => {
    await new Promise((resolve) => setTimeout(resolve, HOLD_MS));
    await route.continue().catch(() => undefined);
  };
  await page.route(MONACO_VS, handler);
  return async () => {
    await page.unroute(MONACO_VS, handler);
    await cdp.send("Network.setCacheDisabled", { cacheDisabled: false });
    await cdp.detach();
  };
}

const cancelations = (watch: Watch) =>
  [...watch.rejections, ...watch.pageErrors].filter((text) => /cancel/i.test(text));

test.describe("functions — safe Monaco loader (blocks-logic#435)", () => {
  test("§10 steps 1–6: early unmounts, completions, no reloads, loader failure, themes", async ({
    page,
  }, testInfo) => {
    test.setTimeout(12 * 60_000);

    const project = readLogicProject();
    expect(project, "suite setup saved the shared project").toBeTruthy();
    const watch = await watchPage(page);
    const editor = new WorkflowEditorPage(page);
    const inspector = new NodeInspectorPage(page);
    const topbar = new TopbarComponent(page);
    const stamp = Date.now();
    const workflowName = `E2E Monaco ${stamp}`;
    let fn: CreatedFunction | undefined;
    let workflow: ImportedWorkflow | undefined;
    let primaryFailure = false;

    const functionUrl = () => fn!.url;
    const codeTab = page.getByRole("tab", { name: /^Code/ }).first();
    const runsTab = page.getByRole("tab", { name: /^Runs/ }).first();
    const codePanel = page
      .getByRole("tabpanel")
      .filter({ has: page.locator(".monaco-editor") })
      .first();

    try {
      await test.step("[Setup] Create a function to open", async () => {
        fn = await createFunctionInUi(page, `e2e-monaco-${stamp}`);
      });

      await test.step("Step 1 (H3, H5, C3): leave Code for Runs before the editors appear — no cancelation rejection", async () => {
        const release = await holdMonaco(page);
        reset(watch);
        await openInFreshDocument(page, functionUrl());
        await expect(runsTab).toBeVisible({ timeout: 60_000 });
        await expect(codeTab).toHaveAttribute("data-state", "active");
        await expect(page.locator(".monaco-editor"), "editors are still loading").toHaveCount(0);
        await runsTab.click();
        await expect(runsTab).toHaveAttribute("data-state", "active");
        await page.waitForTimeout(HOLD_MS + 2_000); // the held loader settles after the editors unmounted
        await release();
        expect(cancelations(watch), "no cancelation rejection").toEqual([]);
      });

      await test.step("Step 2 (H1, H6): back on Code the editor renders, with ctx. completions", async () => {
        await codeTab.click();
        await expect(codeTab).toHaveAttribute("data-state", "active");
        // One Monaco editor shows the selected file (index.js or package.json); "both editors" in the
        // spec are this function editor and the workflow Code node field (step 3).
        await expect(codePanel.locator(".monaco-editor")).toHaveCount(1, { timeout: 60_000 });
        await page.getByRole("button", { name: "package.json", exact: true }).click();
        await expect(codePanel.locator(".monaco-editor")).toHaveCount(1);
        await page.getByRole("button", { name: "index.js", exact: true }).click();
        await expect(codePanel.locator(".monaco-editor")).toHaveCount(1);
        const source = codePanel.locator(".monaco-editor").first();
        await source.locator(".view-lines").click();
        await page.keyboard.press("Control+End");
        await page.keyboard.press("Enter");
        await page.keyboard.type("ctx.");
        const suggestions = source.locator(".suggest-widget .monaco-list-row");
        await expect(suggestions.first(), "ctx. completions").toBeVisible({ timeout: 15_000 });
        await page.keyboard.press("Escape");
        for (let i = 0; i < 3; i++) await page.keyboard.press("Control+Z");
        expect(cancelations(watch)).toEqual([]);
      });

      await test.step("Step 4 (C7, H6): Code → Runs → Code three times loads nothing from /monaco/vs", async () => {
        // Every /monaco/vs request the page makes (Playwright sees the editors' worker scripts too, as the
        // DevTools Network panel lists them). Loader files (loader.js, editor.main.*) would mean Monaco is
        // loaded again (loader.init); worker scripts are started by each editor Monaco creates.
        const requests: string[] = [];
        const count = (request: { url(): string }) => {
          if (MONACO_VS.test(request.url()))
            requests.push(request.url().replace(/^.*\/monaco\/vs\//, ""));
        };
        page.on("request", count);
        for (let i = 0; i < 3; i++) {
          await runsTab.click();
          await expect(runsTab).toHaveAttribute("data-state", "active");
          await codeTab.click();
          await expect(codePanel.locator(".monaco-editor")).toHaveCount(1);
        }
        await page.waitForTimeout(2_000);
        page.off("request", count);
        const workers = requests.filter((url) => /\.worker[-.]/.test(url));
        const loaderFiles = requests.filter((url) => !workers.includes(url));
        await testInfo.attach("monaco-vs-requests.json", {
          body: JSON.stringify({ loaderFiles, workers }, null, 2),
          contentType: "application/json",
        });
        expect(
          loaderFiles,
          "Monaco is not loaded again (no loader.js / editor.main request)",
        ).toEqual([]);
        // §10 step 4 counts every /monaco/vs request. Soft, so steps 6, 3 and 5 still run and report.
        expect
          .soft(workers, "no /monaco/vs request at all after the first load (§10 step 4)")
          .toEqual([]);
      });

      await test.step("Step 6 (H11): dark and back gives the same editor themes", async () => {
        const themeOf = async () =>
          (await codePanel.locator(".monaco-editor").first().getAttribute("class"))
            ?.split(/\s+/)
            .filter((c) => /^vs|dark|light|theme/i.test(c))
            .sort()
            .join(" ") ?? "";
        const before = await themeOf();
        await topbar.switchToDark();
        await topbar.expectThemeApplied("dark");
        await expect
          .poll(themeOf, { message: "the editors switch to a dark theme" })
          .not.toBe(before);
        await topbar.switchToLight();
        await topbar.expectThemeApplied("light");
        await expect.poll(themeOf, { message: "the editors return to their theme" }).toBe(before);
      });

      await test.step("Step 3 (H4, C11, H2): Code node A → B → A before the editor appears, then $ completions", async () => {
        workflow = await importWorkflowFile(page, twoCodeNodesWorkflowFile(workflowName));
        // A full load, so the Monaco instance is not loaded yet in this document.
        await openInFreshDocument(page, workflow.url);
        await expect(editor.canvasNode(CODE_NODE_NAMES.codeA)).toBeVisible({ timeout: 60_000 });
        const release = await holdMonaco(page);
        reset(watch);
        await editor.clickCanvasNode(CODE_NODE_NAMES.codeA);
        await inspector.expectOpenFor(CODE_NODE_NAMES.codeA);
        await inspector.close();
        await editor.clickCanvasNode(CODE_NODE_NAMES.codeB);
        await inspector.expectOpenFor(CODE_NODE_NAMES.codeB);
        await inspector.close();
        await editor.clickCanvasNode(CODE_NODE_NAMES.codeA);
        await inspector.expectOpenFor(CODE_NODE_NAMES.codeA);
        await page.waitForTimeout(HOLD_MS + 2_000);
        await release();
        const codeEditor = inspector.dialog.locator(".monaco-editor").first();
        await expect(codeEditor, "Code A's editor shows").toBeVisible({ timeout: 60_000 });
        expect(cancelations(watch), "no cancelation rejection").toEqual([]);

        await codeEditor.locator(".view-lines").click();
        await page.keyboard.press("Control+End");
        await page.keyboard.press("Enter");
        await page.keyboard.type("$");
        const suggestions = codeEditor.locator(".suggest-widget .monaco-list-row");
        await expect(
          suggestions.filter({ hasText: "$json" }).first(),
          "$json completion",
        ).toBeVisible({ timeout: 15_000 });
        await expect(
          suggestions.filter({ hasText: "$items" }).first(),
          "$items completion",
        ).toBeVisible();
        // Escape closes the suggestions (and may close the inspector sheet too).
        await page.keyboard.press("Escape");
        await inspector.closeIfOpen();
      });

      await test.step("Step 5 (C1): loader.js blocked — error logged, no uncaught rejection", async () => {
        const block = (route: Route) => route.abort("blockedbyclient");
        await page.route(/\/monaco\/vs\/loader\.js/, block);
        reset(watch);
        await openInFreshDocument(page, functionUrl());
        await expect(codeTab).toBeVisible({ timeout: 60_000 });
        await expect
          .poll(
            () =>
              watch.consoleErrors.some((text) => text.startsWith("Monaco initialization: error:")),
            {
              timeout: 30_000,
              message: "Monaco initialization: error: in the console",
            },
          )
          .toBe(true);
        await page.waitForTimeout(2_000);
        expect(watch.rejections, "no unhandled rejection").toEqual([]);
        expect(watch.pageErrors, "no uncaught page error").toEqual([]);
        await page.unroute(/\/monaco\/vs\/loader\.js/, block);
      });
    } catch (error) {
      primaryFailure = true;
      throw error;
    } finally {
      await test.step("[Cleanup] Delete the fixtures this spec created", async () => {
        // Cleanup (§10): nothing else changed; remove the fixtures.
        const failures: string[] = [];
        await page.unrouteAll({ behavior: "ignoreErrors" }).catch(() => undefined);
        try {
          if (workflow) await deleteWorkflowAt(page, workflow);
          else await deleteWorkflowNamed(page, workflowName).catch(() => undefined);
        } catch (cleanupError) {
          failures.push(`workflow: ${String(cleanupError)}`);
        }
        try {
          if (fn) await deleteFunctionInUi(page, fn);
        } catch (cleanupError) {
          failures.push(`function: ${String(cleanupError)}`);
        }
        if (failures.length && !primaryFailure)
          throw new Error(`cleanup failed: ${failures.join("; ")}`);
        if (failures.length) console.warn(`[cleanup] ${failures.join("; ")}`);
      });
    }
  });
});

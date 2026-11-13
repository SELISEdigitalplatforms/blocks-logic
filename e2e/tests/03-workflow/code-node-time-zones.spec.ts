import { test, expect } from "../../support/test-base";
import { CODE_NODE_NAMES, timeZoneWorkflowFile } from "../../support/code-node-fixtures";
import {
  deleteWorkflowAt,
  deleteWorkflowNamed,
  importWorkflowFile,
  type ImportedWorkflow,
} from "../../support/workflow-import";
import { NodeInspectorPage, WorkflowEditorPage } from "../../po";

/**
 * Code node IANA time zones on the deployed Api image — blocks-logic#436 §10 steps 1–2 (H11, H1–H6).
 *
 * The fixture (support/code-node-fixtures.ts) is Webhook → "Time Zones", an all-items Code node whose
 * script returns the LTG #11985 reproduction values (current time in Asia/Dhaka and Europe/Zurich, the
 * resolved zone, the error for `Not/AZone`) and the §8 H1–H6 expressions. The Webhook answers its test
 * call with the last node's output ("last" mode), so the run happens in the deployed Api, whose image
 * carries the zone data (H9, C11). Steps 3–5 (local image builds, CI) are outside the browser suite.
 */

type ZoneRow = {
  iso: string;
  dhaka: string;
  zurich: string;
  resolved: string;
  bad: string;
  h1: string;
  h2: string;
  h3: string;
  h4: string;
  h5: string;
  h6: string;
};

/** The first object anywhere in `value` that carries the script's `iso` and `h1` fields. */
function findZoneRow(value: unknown): ZoneRow | undefined {
  if (Array.isArray(value)) {
    for (const entry of value) {
      const row = findZoneRow(entry);
      if (row) return row;
    }
    return undefined;
  }
  if (value && typeof value === "object") {
    const record = value as Record<string, unknown>;
    if (typeof record.iso === "string" && typeof record.h1 === "string") return record as ZoneRow;
    for (const nested of Object.values(record)) {
      const row = findZoneRow(nested);
      if (row) return row;
    }
  }
  return undefined;
}

/** "HH:MM" (24 h) of `instant` in `timeZone`, from this test runner's own zone data. */
function hourMinute(instant: Date, timeZone: string): string {
  const parts = new Intl.DateTimeFormat("en-GB", {
    timeZone,
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).formatToParts(instant);
  const part = (type: string) => parts.find((p) => p.type === type)?.value ?? "";
  return `${part("hour")}:${part("minute")}`;
}

/** Minutes east of UTC that "HH:MM" in a zone is at `instant`, folded into -12 h..+14 h. */
function offsetMinutes(instant: Date, local: string): number {
  const [hours, minutes] = local.split(":").map(Number);
  let diff = hours * 60 + minutes - (instant.getUTCHours() * 60 + instant.getUTCMinutes());
  if (diff < -12 * 60) diff += 24 * 60;
  if (diff > 14 * 60) diff -= 24 * 60;
  return diff;
}

test.describe("workflow — Code node time zones (blocks-logic#436)", () => {
  test("§10 steps 1–2: LTG #11985 reproduction and H1–H6 on the deployed Api", async ({
    page,
  }, testInfo) => {
    test.setTimeout(8 * 60_000);

    const editor = new WorkflowEditorPage(page);
    const inspector = new NodeInspectorPage(page);
    const name = `E2E Time Zones ${Date.now()}`;
    let imported: ImportedWorkflow | undefined;
    let primaryFailure = false;

    try {
      imported =
        await test.step("[Setup] Import Webhook → Time Zones (all-items Code node)", async () => {
          const workflow = await importWorkflowFile(page, timeZoneWorkflowFile(name));
          await expect(editor.canvasNode(CODE_NODE_NAMES.webhook)).toBeVisible();
          await expect(editor.canvasNode(CODE_NODE_NAMES.zones)).toBeVisible();
          return workflow;
        });

      const webhookUrl = await test.step("[Setup] Read the Webhook test URL", async () => {
        await editor.clickCanvasNode(CODE_NODE_NAMES.webhook);
        await inspector.expectOpenFor(CODE_NODE_NAMES.webhook);
        const url = await inspector.deprecatedWebhookUrlInput.inputValue();
        expect(url, "webhook test URL with tenant, workflow and node in the path").toMatch(
          /\/Workflow\/webhook-test\/[^/]+\/[^/]+\/[^/]+$/,
        );
        await inspector.close();
        return url;
      });

      const row = await test.step("Run the workflow through its Webhook test call", async () => {
        await editor.startTestListener();
        const response = await page.request.post(webhookUrl, { data: [{ probe: "time zones" }] });
        expect(response.ok(), `webhook-test answered HTTP ${response.status()}`).toBeTruthy();
        const body = (await response.json()) as { status?: string; data?: unknown };
        await testInfo.attach("time-zone-output.json", {
          body: JSON.stringify(body.data ?? null, null, 2),
          contentType: "application/json",
        });
        const found = findZoneRow(body.data);
        expect(
          found,
          `the Time Zones node output is in the response (status ${body.status})`,
        ).toBeTruthy();
        return found as ZoneRow;
      });

      await test.step("Step 1 (H11): Dhaka at UTC+6, Zurich at UTC+1 or +2, resolved zone, unknown zone error", async () => {
        const instant = new Date(row.iso);
        expect(Number.isNaN(instant.getTime()), `iso "${row.iso}" is a date`).toBe(false);
        expect(row.dhaka, "Asia/Dhaka must format at UTC+6").toBe(
          hourMinute(instant, "Asia/Dhaka"),
        );
        expect(offsetMinutes(instant, row.dhaka), "Asia/Dhaka offset").toBe(6 * 60);
        expect(row.zurich, "Europe/Zurich must format at its offset for the run date").toBe(
          hourMinute(instant, "Europe/Zurich"),
        );
        expect([60, 120], "Europe/Zurich is at UTC+1 or UTC+2").toContain(
          offsetMinutes(instant, row.zurich),
        );
        expect(row.resolved).toBe("Asia/Dhaka");
        expect(row.bad).toContain("Invalid time zone: Not/AZone");
      });

      await test.step("Step 2 (H1–H6): the §8 expressions give the exact strings", async () => {
        expect(row.h1, "H1 Asia/Dhaka must format at UTC+6").toBe("28/10/2026, 5:54:01 pm");
        expect(row.h2, "H2 Europe/Zurich in summer must format at UTC+2").toBe(
          "1/7/2026, 2:00:00 pm",
        );
        expect(row.h3, "H3 Europe/Zurich in winter must format at UTC+1").toBe(
          "15/1/2026, 1:00:00 pm",
        );
        expect(row.h4, "H4 Asia/Kathmandu must format at UTC+5:45").toBe("05:45 am");
        expect(row.h5, "H5 Asia/Dhaka date must roll to the next day").toBe("29/10/2026");
        expect(row.h6, "H6 Asia/Thimphu must format at UTC+6").toBe("6:00:00 am");
      });
    } catch (error) {
      primaryFailure = true;
      throw error;
    } finally {
      await test.step("[Cleanup] Delete the fixtures this spec created", async () => {
        // Cleanup (§10): delete the test workflow, also when a step failed.
        try {
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

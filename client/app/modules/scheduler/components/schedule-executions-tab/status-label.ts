import { ScheduleTriggerType } from "../../types/schedule.service.type";

const EMPTY = "—";

/** The status chip text (SPEC7 §5.7): "HTTP {status}", or "—" for a legacy row with no status. */
export function statusLabel(status: number | null | undefined): string {
  return typeof status === "number" ? `HTTP ${status}` : EMPTY;
}

/** As {@link statusLabel}, but an accepted queue send reads "Queued" (sheet only). */
export function sheetStatusLabel(
  status: number | null | undefined,
  triggerType: number | null | undefined,
): string {
  if (triggerType === ScheduleTriggerType.Queue && status === 202) return "Queued";
  return statusLabel(status);
}

/** Bodies the scheduler wrote itself when the target gave no answer start with one of these. */
export const SCHEDULER_BODY_PREFIXES = [
  "Webhook is not configured.",
  "Network error:",
  "Timed out:",
  "Cancelled:",
  "Unexpected error:",
  "Queue is not configured.",
  "Queue send failed:",
] as const;

export function isSchedulerRecorded(body: string | null | undefined): boolean {
  return !!body && SCHEDULER_BODY_PREFIXES.some((prefix) => body.startsWith(prefix));
}

export const TRUNCATION_MARKER = "…[truncated]";

export function isTruncated(body: string | null | undefined): boolean {
  return !!body && body.endsWith(TRUNCATION_MARKER);
}

/** "{n} ms" below a second, "{x.x} s" from 1,000 ms, "—" when unknown. */
export function formatDuration(ms: number | null | undefined): string {
  if (typeof ms !== "number" || Number.isNaN(ms)) return EMPTY;
  if (ms < 1000) return `${Math.round(ms)} ms`;
  return `${(ms / 1000).toFixed(1)} s`;
}

import { Card } from "@/components/ui-kits/card/card";
import { formatDuration } from "../../utils/format";
import { IRunDetail, IRunTiming } from "../../types/run.types";
import { useT, tStatic } from "@seliseblocks/genesis-os";

/**
 * Where a call's time went, stage by stage — kept apart from the run's metrics so the sandbox
 * metric only says warm or cold. Each stage is reported by the part that did it: the Api (before
 * the run was queued), the runner (time on the queue, hand-over to the sandbox), the sandbox (the
 * function's own code), the Worker (saving the answer). Times only, never values.
 */
const STAGES: { group: string; label: string; hint: string }[] = [
  { group: "api", get label() { return tStatic("logic.functions.components", "RUN_TIMING.API_BEFORE_QUEUE"); }, get hint() { return tStatic("logic.functions.components", "RUN_TIMING.CHECKS_THE_CALLER_LOADS"); } },
  { group: "queue", get label() { return tStatic("logic.functions.components", "RUN_TIMING.WAITING_FOR_A_RUNNER"); }, get hint() { return tStatic("logic.functions.components", "RUN_TIMING.ON_THE_QUEUE_UNTIL"); } },
  { group: "handover", get label() { return tStatic("logic.functions.components", "RUN_TIMING.HANDOVER_TO_SANDBOX"); }, get hint() { return tStatic("logic.functions.components", "RUN_TIMING.RUNNER_ADMISSION_SANDBOX_SECRETS"); } },
  { group: "code", get label() { return tStatic("logic.functions.components", "RUN_TIMING.YOUR_CODE"); }, get hint() { return tStatic("logic.functions.components", "RUN_TIMING.THE_HANDLER_INSIDE_THE"); } },
  { group: "result", get label() { return tStatic("logic.functions.components", "RUN_TIMING.SAVING_THE_RESULT"); }, get hint() { return tStatic("logic.functions.components", "RUN_TIMING.WORKER_WAITS_WRITES_THE"); } },
];

interface TimingRow {
  label: string;
  hint: string;
  totalMs: number;
  steps: { step: string; ms: number }[];
}

export const buildTimingRows = (run: IRunDetail): TimingRow[] => {
  const timings: IRunTiming[] = run.timings ?? [];
  const rows: TimingRow[] = [];

  for (const stage of STAGES) {
    if (stage.group === "code") {
      if (run.durationMs != null) {
        rows.push({ label: stage.label, hint: stage.hint, totalMs: run.durationMs, steps: [] });
      }
      continue;
    }

    const inGroup = timings.filter((t) => t.group === stage.group);
    // A run from before timings existed still reported its hand-over as one figure.
    if (inGroup.length === 0) {
      if (stage.group === "handover" && run.handoverMs != null) {
        rows.push({ label: stage.label, hint: stage.hint, totalMs: run.handoverMs, steps: [] });
      }
      continue;
    }

    // The runner reports the hand-over's own total (claim → input on stdin), which is the honest
    // figure: its steps overlap (secrets and the token are fetched side by side) and do not add up.
    const reportedTotal = inGroup.find((t) => t.step === "total")?.ms;
    const steps = inGroup
      .filter((t) => t.step !== "total" && t.step !== stage.group)
      .map((t) => ({ step: t.step, ms: t.ms }));
    const sum = inGroup.filter((t) => t.step !== "total").reduce((acc, t) => acc + t.ms, 0);
    rows.push({ label: stage.label, hint: stage.hint, totalMs: reportedTotal ?? sum, steps });
  }

  return rows;
};

export const RunTiming = ({ run }: Readonly<{ run: IRunDetail }>) => {
  const { t: tr } = useT("logic.functions.components");
  const rows = buildTimingRows(run);
  if (rows.length === 0) return null;

  const total = rows.reduce((acc, row) => acc + row.totalMs, 0);
  const longest = Math.max(...rows.map((row) => row.totalMs), 1);

  return (
    <Card className="overflow-hidden" data-testid="run-timing">
      <div className="flex flex-wrap items-baseline justify-between gap-2 border-b px-4 py-3">
        <span className="text-sm font-semibold">{tr("RUN_TIMING.TIMING")}</span>
        <span className="text-xs text-medium-emphasis">{tr("RUN_TIMING.IN_TOTAL", { value: formatDuration(total) })}</span>
      </div>
      {rows.map((row) => (
        <div key={row.label} className="flex flex-col gap-1 border-b px-4 py-2.5 last:border-b-0">
          <div className="flex flex-wrap items-center gap-3">
            <span className="w-[160px] shrink-0 text-xs font-semibold" title={row.hint}>
              {row.label}
            </span>
            <div className="h-1.5 min-w-[60px] flex-1 overflow-hidden rounded bg-border" aria-hidden="true">
              <div
                className="h-full rounded bg-primary"
                style={{ width: `${Math.max(2, (row.totalMs / longest) * 100)}%` }}
              />
            </div>
            <span className="w-[70px] shrink-0 text-right text-xs font-semibold">
              {formatDuration(row.totalMs)}
            </span>
          </div>
          {row.steps.length > 0 && (
            <span className="break-words text-[10px] text-low-emphasis">
              {row.steps.map((s) => `${s.step} ${s.ms} ms`).join(" · ")}
            </span>
          )}
        </div>
      ))}
    </Card>
  );
};

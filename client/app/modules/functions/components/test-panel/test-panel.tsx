import { useCallback, useEffect, useState } from "react";
import { Loader2, Play } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { cn } from "@/lib/utils";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { RunStatusChip } from "../run-status-chip";
import { useGetRun, useGetRunLogs, useTestFunction } from "../../hooks/use-runs";
import { useFunctionEditorStore } from "../../store/function-editor-store";
import { explainRunError } from "../../constants/run-error.constant";
import { TERMINAL_RUN_STATUSES } from "../../types/run.types";

type TestPanelProps = {
  functionId: string;
  /** Most recent run, so its input can be reused as the test payload. */
  lastRunId?: string;
  onOpenRun: (runId: string) => void;
  /** Saves the working copy first — a test always runs the code that is on screen. */
  onBeforeRun?: () => Promise<void>;
};

const STAGES = ["Saving", "Queued", "Running", "Done"] as const;

const stageIndex = (isSaving: boolean, status?: string) => {
  if (isSaving) return 0;
  if (!status) return 1;
  if (TERMINAL_RUN_STATUSES.includes(status as never)) return 3;
  if (status === "Queued" || status === "Claimed") return 1;
  return 2;
};

const LOG_LEVEL_CLASS: Record<string, string> = {
  error: "text-error",
  warn: "text-warning-800",
  info: "text-primary",
  debug: "text-low-emphasis",
};

const formatDuration = (durationMs?: number | null) =>
  durationMs == null ? null : durationMs < 1000 ? `${durationMs} ms` : `${(durationMs / 1000).toFixed(2)} s`;

const formatMemory = (peakMemoryBytes?: number | null) =>
  peakMemoryBytes == null ? null : `${Math.round(peakMemoryBytes / (1024 * 1024))} MB`;

/** Test input + the result of the last test, in the Code tab's right rail. Ctrl+Enter runs it. */
export const TestPanel = ({ functionId, lastRunId, onOpenRun, onBeforeRun }: TestPanelProps) => {
  const testInput = useFunctionEditorStore((s) => s.testInput);
  const setTestInput = useFunctionEditorStore((s) => s.setTestInput);
  const [runId, setRunId] = useState<string | null>(null);
  const [inputError, setInputError] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  const { mutateAsync, isPending } = useTestFunction();
  const { data: run } = useGetRun({ runId: runId ?? undefined });
  const { data: logs } = useGetRunLogs(runId ?? undefined, 0, 8);
  const { data: lastRun } = useGetRun({ runId: lastRunId, enabled: !!lastRunId });

  const isActive = isPending || isSaving || (!!run && !TERMINAL_RUN_STATUSES.includes(run.status));

  const handleRun = useCallback(async () => {
    try {
      JSON.parse(testInput || "{}");
    } catch {
      return setInputError("That is not valid JSON.");
    }
    setInputError(null);
    try {
      if (onBeforeRun) {
        setIsSaving(true);
        await onBeforeRun();
      }
      const response = await mutateAsync({ functionId, inputJson: testInput });
      setRunId(response.runId);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to run test" });
    } finally {
      setIsSaving(false);
    }
  }, [functionId, mutateAsync, onBeforeRun, testInput]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (!(event.metaKey || event.ctrlKey) || event.key !== "Enter") return;
      event.preventDefault();
      if (!isActive) void handleRun();
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isActive, handleRun]);

  // The header's "Test run" button raises a request on the editor store; this is the subscription
  // that answers it, so the button works from any tab without the panel being lifted into the page.
  useEffect(
    () =>
      useFunctionEditorStore.subscribe((state, previous) => {
        if (state.testRunRequestedAt !== previous.testRunRequestedAt) void handleRun();
      }),
    [handleRun],
  );

  const formatInput = () => {
    try {
      setTestInput(JSON.stringify(JSON.parse(testInput || "{}"), null, 2));
      setInputError(null);
    } catch {
      setInputError("That is not valid JSON.");
    }
  };

  const currentStage = stageIndex(isSaving, run?.status);
  const meta = [formatDuration(run?.durationMs), formatMemory(run?.peakMemoryBytes), run?.versionNumber ? `v${run.versionNumber}` : null]
    .filter(Boolean)
    .join(" · ");
  const explanation = explainRunError(run?.errorCode, run?.errorMessage);

  return (
    <div className="flex flex-col gap-3">
      <Card className="overflow-hidden">
        <div className="flex items-center justify-between gap-3 border-b px-4 py-3">
          <span className="text-sm font-semibold">Test input</span>
          <span className="text-xs text-low-emphasis">same sandbox as production</span>
        </div>
        <Textarea
          aria-label="Test input JSON"
          spellCheck={false}
          className="min-h-[150px] resize-y rounded-none border-0 font-mono text-xs focus-visible:ring-0 focus-visible:ring-offset-0"
          value={testInput}
          onChange={(e) => setTestInput(e.target.value)}
        />
        {inputError && <p className="px-4 pb-2 text-xs text-error">{inputError}</p>}
        <div className="flex items-center justify-between gap-2 border-t px-4 py-2">
          <div className="flex items-center gap-1">
            <Button variant="ghost" size="xs" className="px-2 text-xs" onClick={formatInput}>
              Format
            </Button>
            <Button
              variant="ghost"
              size="xs"
              className="px-2 text-xs"
              disabled={!lastRun?.input}
              onClick={() => lastRun?.input && setTestInput(lastRun.input)}
            >
              Use last run input
            </Button>
          </div>
        </div>
        <div className="border-t px-4 py-3">
          <Button className="w-full gap-1.5" disabled={isActive} onClick={() => void handleRun()}>
            {isActive ? <Loader2 className="h-4 w-4 animate-spin" /> : <Play className="h-4 w-4" />}
            {isActive ? "Running…" : runId ? "Run again" : "Run test"}
          </Button>
        </div>
      </Card>

      {(isActive || run) && (
        <Card className="overflow-hidden">
          <div className="flex items-center justify-between gap-3 border-b px-4 py-2.5">
            <div className="flex min-w-0 items-center gap-2">
              {run ? (
                <RunStatusChip status={run.status} />
              ) : (
                <span className="text-xs font-semibold text-medium-emphasis">Starting…</span>
              )}
              {!!meta && <span className="truncate text-xs text-medium-emphasis">{meta}</span>}
            </div>
            {runId && (
              <button
                type="button"
                className="shrink-0 text-xs font-semibold text-primary hover:underline"
                onClick={() => onOpenRun(runId)}
              >
                Open run ›
              </button>
            )}
          </div>

          <div className="flex items-center gap-1.5 border-b px-4 py-2.5">
            {STAGES.map((stage, index) => (
              <span key={stage} className="flex items-center gap-1.5">
                <span
                  className={cn(
                    "text-xs",
                    index < currentStage && "text-medium-emphasis",
                    index === currentStage && "font-semibold text-primary",
                    index > currentStage && "text-low-emphasis",
                  )}
                >
                  {stage}
                </span>
                {index < STAGES.length - 1 && <span className="text-xs text-low-emphasis">›</span>}
              </span>
            ))}
          </div>

          {explanation && (
            <p className="border-b bg-error/5 px-4 py-2.5 text-xs leading-relaxed text-error">
              {run?.errorCode && <span className="font-mono font-semibold">{run.errorCode}</span>}{" "}
              {explanation}
            </p>
          )}

          <CardContent className="max-h-52 overflow-auto bg-surface-app p-4">
            {(logs?.data ?? []).length === 0 ? (
              <p className="text-xs text-low-emphasis">No log lines yet.</p>
            ) : (
              <div className="flex flex-col gap-1">
                {(logs?.data ?? []).map((line) => (
                  <div key={line.seq} className="flex gap-2 font-mono text-[11px] leading-relaxed">
                    <span
                      className={cn(
                        "shrink-0 font-semibold uppercase",
                        LOG_LEVEL_CLASS[line.level.toLowerCase()] ?? "text-medium-emphasis",
                      )}
                    >
                      {line.level}
                    </span>
                    <span className="min-w-0 break-words">{line.message}</span>
                  </div>
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
};

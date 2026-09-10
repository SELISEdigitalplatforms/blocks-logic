import { useCallback, useEffect, useRef, useState } from "react";
import { Loader2, Play } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { cn } from "@/lib/utils";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { RunStatusChip } from "../run-status-chip";
import { useGetRun, useGetRunLogs, useTestFunction } from "../../hooks/use-runs";
import { useGetBuild } from "../../hooks/use-versions";
import { BuildProgress } from "../build-progress";
import { useFunctionEditorStore } from "../../store/function-editor-store";
import { explainRunError } from "../../constants/run-error.constant";
import { TERMINAL_RUN_STATUSES } from "../../types/run.types";
import { formatDuration, formatMegabytes } from "../../utils/format";

type TestPanelProps = {
  functionId: string;
  /** Most recent run, so its input can be reused as the test payload. */
  lastRunId?: string;
  onOpenRun: (runId: string) => void;
  /**
   * Saves the working copy first — a test always runs the code that is on screen. Returning false
   * stops the run: testing the previous source after a failed save is worse than not testing.
   */
  onBeforeRun?: () => Promise<boolean>;
};

/** §4.4's stepper. "Building" is the slow one — the image build gates every test. */
const STAGES = ["Saving", "Building", "Queued", "Running", "Done"] as const;

/**
 * `Test` builds the image before it queues anything, and answers only once a run exists, so the
 * window with a request in flight and no run yet *is* the build. Without that stage the wait shows
 * as "Queued" and a slow build looks like a stall.
 */
const stageIndex = (isSaving: boolean, isBuilding: boolean, status?: string) => {
  if (isSaving) return 0;
  if (!status) return isBuilding ? 1 : 2;
  if (TERMINAL_RUN_STATUSES.includes(status as never)) return 4;
  if (status === "Queued" || status === "Claimed") return 2;
  return 3;
};

const LOG_LEVEL_CLASS: Record<string, string> = {
  error: "text-error",
  warn: "text-warning-800",
  info: "text-primary",
  debug: "text-low-emphasis",
};

/** Test input + the result of the last test, in the Code tab's right rail. Ctrl+Enter runs it. */
export const TestPanel = ({ functionId, lastRunId, onOpenRun, onBeforeRun }: TestPanelProps) => {
  const testInput = useFunctionEditorStore((s) => s.testInput);
  const setTestInput = useFunctionEditorStore((s) => s.setTestInput);
  const [runId, setRunId] = useState<string | null>(null);
  // Set when Test came back with a build rather than a run: the image was not ready yet.
  const [buildId, setBuildId] = useState<string | null>(null);
  const [inputError, setInputError] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const autoRanForBuild = useRef<string | null>(null);

  const { mutateAsync, isPending } = useTestFunction();
  const { data: run } = useGetRun({ runId: runId ?? undefined });
  const { data: logs } = useGetRunLogs(runId ?? undefined, 0, 8);
  const { data: lastRun } = useGetRun({ runId: lastRunId, enabled: !!lastRunId });
  // Already polls itself every 2 s while queued or building.
  const { data: build } = useGetBuild(buildId ?? undefined);

  const isBuildInFlight = build?.status === "Queued" || build?.status === "Building";
  const isActive =
    isPending ||
    isSaving ||
    isBuildInFlight ||
    (!!run && !TERMINAL_RUN_STATUSES.includes(run.status));

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
        const saved = await onBeforeRun();
        if (!saved) return;
      }
      const response = await mutateAsync({ functionId, inputJson: testInput });
      if (response.runId) {
        setBuildId(null);
        setRunId(response.runId);
      } else if (response.buildId) {
        // No run yet — the image is still building. Follow the build and start the run when it lands.
        setRunId(null);
        setBuildId(response.buildId);
      }
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to run test" });
    } finally {
      setIsSaving(false);
    }
  }, [functionId, mutateAsync, onBeforeRun, testInput]);

  useEffect(() => {
    if (!buildId || build?.status !== "Succeeded") return;
    if (autoRanForBuild.current === buildId) return;
    autoRanForBuild.current = buildId;
    void handleRun();
  }, [buildId, build?.status, handleRun]);

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

  const currentStage = stageIndex(isSaving, isPending || isBuildInFlight, run?.status);
  const meta = [
    run?.durationMs == null ? null : formatDuration(run.durationMs),
    run?.peakMemoryBytes == null ? null : formatMegabytes(run.peakMemoryBytes),
    run?.versionNumber ? `v${run.versionNumber}` : null,
  ]
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

      {(isActive || run || buildId) && (
        <Card className="overflow-hidden">
          <div className="flex items-center justify-between gap-3 border-b px-4 py-2.5">
            <div className="flex min-w-0 items-center gap-2">
              {run ? (
                <RunStatusChip status={run.status} />
              ) : (
                <span className="text-xs font-semibold text-medium-emphasis">
                  {buildId ? "Building…" : "Starting…"}
                </span>
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

          {buildId && !run && (
            <div className="border-b px-4 py-2.5">
              <BuildProgress buildId={buildId} />
            </div>
          )}

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

import { useCallback, useEffect, useRef, useState } from "react";
import { CheckCircle2, Circle, Clock, Loader2, Play, Square, XCircle } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { cn } from "@/lib/utils";
import { getErrorMessage, isErrorWithErrors } from "@/lib/error";
import { RunStatusChip } from "../run-status-chip";
import {
  useCancelRun,
  useGetRun,
  useGetRunLogs,
  useLatestTestRun,
  useTestFunction,
} from "../../hooks/use-runs";
import { useGetBuild } from "../../hooks/use-versions";
import { BuildProgress } from "../build-progress";
import { useFunctionEditorStore } from "../../store/function-editor-store";
import { explainRunError } from "../../constants/run-error.constant";
import { TERMINAL_RUN_STATUSES } from "../../types/run.types";
import { formatDuration, formatMegabytes } from "../../utils/format";
import { payloadOf } from "../../utils/test-input";
import { testHttpVerb } from "../../constants/endpoint.constant";

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

/**
 * What the developer sees while a test runs, one row per real step. Each maps to the run's own
 * status, so the row that spins is what the platform is doing now, never a guess.
 */
const STEPS = [
  { title: "Save code", hint: "Saving the code on screen, so the test runs exactly that." },
  {
    title: "Wait for a runner",
    hint: "The test is queued. A runner picks it up when it has a free slot.",
  },
  {
    title: "Build and start sandbox",
    hint: "Installing your packages and starting a fresh sandbox. Every test builds its own image, so this is the slow step.",
  },
  {
    title: "Run handler",
    hint: "Your handler is running. Its logs and result appear when it ends.",
  },
  { title: "Result", hint: "" },
] as const;

const stepIndex = (isSaving: boolean, isBuilding: boolean, status?: string) => {
  if (isSaving) return 0;
  if (status && TERMINAL_RUN_STATUSES.includes(status as never)) return 4;
  if (isBuilding) return 2;
  if (!status || status === "Queued") return 1;
  if (status === "Claimed" || status === "Starting") return 2;
  return 3;
};

/** The server says "try again in 87s"; fall back to a minute when the wording changes. */
const retryAfterSeconds = (message: string) => {
  const match = /in (\d+)\s*s/i.exec(message);
  return match ? Number(match[1]) : 60;
};

const errorText = (error: unknown) => {
  if (!isErrorWithErrors(error)) return "The test could not be started.";
  const { errors } = error;
  // The Test endpoint's refusals are { code, message }; the rest are { field: message }.
  if (typeof errors.message === "string") return errors.message;
  const messages = getErrorMessage(errors);
  return Array.isArray(messages) ? messages.join(" ") : messages;
};

const statusOf = (error: unknown) =>
  typeof error === "object" && error !== null && "status" in error
    ? Number((error as { status: unknown }).status)
    : undefined;

const prettyJson = (text?: string | null) => {
  if (text == null || text === "") return null;
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return text;
  }
};

/** How old a still-running test can be and still be picked up again after a reload. */
const RESUME_WINDOW_MS = 10 * 60 * 1000;

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
  const httpMethod = useFunctionEditorStore((s) => s.trigger.httpMethod);
  const httpMethods = useFunctionEditorStore((s) => s.trigger.httpMethods);
  const testVerb = testHttpVerb({ httpMethod, httpMethods });
  const [runId, setRunId] = useState<string | null>(null);
  // Set when Test came back with a build rather than a run: the image was not ready yet.
  const [buildId, setBuildId] = useState<string | null>(null);
  const [inputError, setInputError] = useState<string | null>(null);
  // Why the last Test request was refused, shown in the panel rather than a toast that vanishes.
  const [requestError, setRequestError] = useState<string | null>(null);
  // The server allows one test per function per window; until then the button says how long.
  const [cooldownUntil, setCooldownUntil] = useState<number | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [startedAt, setStartedAt] = useState<number | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const autoRanForBuild = useRef<string | null>(null);
  // The newest test before this click — a run with another id is the one this click started.
  const [latestBeforeClick, setLatestBeforeClick] = useState<string | null | undefined>(undefined);
  // Re-entry guard: the header button, Ctrl+Enter and the panel button all reach handleRun, and a
  // second request while one is open only earns a 429 from the server's per-function test limit.
  const inFlight = useRef(false);

  const { mutateAsync, isPending } = useTestFunction();
  const { mutate: cancelRun, isPending: isCancelling } = useCancelRun();
  const { data: run } = useGetRun({ runId: runId ?? undefined });
  const isRunLive = !!run && !TERMINAL_RUN_STATUSES.includes(run.status);
  // Logs are recorded with the result, so there is nothing to follow while the run is live.
  const { data: logs, refetch: refetchLogs } = useGetRunLogs(runId ?? undefined, 0, 50);
  const { data: latestTest, refetch: refetchLatestTest } = useLatestTestRun(functionId, {
    fast: isPending && !runId,
  });
  // Read once for "Use last run input" — the input never changes, so there is nothing to follow.
  const { data: lastRun } = useGetRun({ runId: lastRunId, enabled: !!lastRunId, poll: false });
  // Already polls itself every 2 s while queued or building.
  const { data: build } = useGetBuild(buildId ?? undefined);

  const isBuildInFlight = build?.status === "Queued" || build?.status === "Building";
  const isActive = isPending || isSaving || isBuildInFlight || isRunLive;
  const cooldownSeconds =
    cooldownUntil && cooldownUntil > now ? Math.ceil((cooldownUntil - now) / 1000) : 0;

  // The Test request answers only when its run ends. Pick the run up as soon as it exists — while
  // the request is open, or a test still running from before a reload or a refused click. Set
  // during render (React's "adjust state when inputs change"), guarded so it settles in one pass.
  if (!runId && latestTest) {
    const isNewForThisClick =
      isPending && latestBeforeClick !== undefined && latestTest.id !== latestBeforeClick;
    // Only a recent one: an old run stuck "Running" is not a test in progress.
    const isRecentLive =
      !isPending &&
      !TERMINAL_RUN_STATUSES.includes(latestTest.status) &&
      now - new Date(latestTest.createdDate).getTime() < RESUME_WINDOW_MS;
    if (isNewForThisClick || isRecentLive) setRunId(latestTest.id);
  }

  // The last log lines are written as the run ends; read them once more when it settles.
  const runStatus = run?.status;
  useEffect(() => {
    if (runStatus && TERMINAL_RUN_STATUSES.includes(runStatus)) void refetchLogs();
  }, [runStatus, refetchLogs]);

  // One clock for the elapsed time and the countdown; it stops once neither is on screen.
  const isCoolingDown = cooldownSeconds > 0;
  useEffect(() => {
    if (!isActive && !isCoolingDown) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [isActive, isCoolingDown]);

  /**
   * Every test builds its own image on the runner that runs it, and the image is deleted when the
   * run ends — so there is nothing cached to reuse and no "rebuild" to ask for.
   */
  const handleRun = useCallback(
    async (options?: { fromBuild?: boolean }) => {
      if (inFlight.current) return;
      try {
        JSON.parse(testInput || "{}");
      } catch {
        return setInputError("That is not valid JSON.");
      }
      setInputError(null);
      setRequestError(null);
      inFlight.current = true;
      if (!options?.fromBuild) {
        setRunId(null);
        setBuildId(null);
        setStartedAt(Date.now());
        setNow(Date.now());
      }
      try {
        if (onBeforeRun) {
          setIsSaving(true);
          const saved = await onBeforeRun();
          setIsSaving(false);
          if (!saved) return;
        }
        setLatestBeforeClick((await refetchLatestTest()).data?.id ?? null);
        const response = await mutateAsync({ functionId, inputJson: testInput });
        if (response.runId) {
          // A test is one job: its run and the build that feeds it come back together. The build
          // is followed for its progress and log only — the run is already queued, so it must not
          // be started a second time when the build lands.
          if (response.buildId) autoRanForBuild.current = response.buildId;
          setBuildId(response.buildId ?? null);
          setRunId(response.runId);
        } else if (response.buildId) {
          // No run yet — the image is still building. Follow the build and start the run when it lands.
          setRunId(null);
          setBuildId(response.buildId);
        }
      } catch (error) {
        const message = errorText(error);
        const status = statusOf(error);
        if (status === 429) {
          setCooldownUntil(Date.now() + retryAfterSeconds(message) * 1000);
          // The earlier test may still be running — show it instead of an empty panel.
          void refetchLatestTest();
        }
        const runIdInError =
          isErrorWithErrors(error) && typeof error.errors.runId === "string"
            ? error.errors.runId
            : null;
        if (runIdInError) setRunId(runIdInError);
        // A 429's own text names the test window, which is not a decided number: say it plainly.
        setRequestError(
          status === 429
            ? "This function was tested a moment ago. Wait for the countdown, then run it again."
            : message,
        );
      } finally {
        setIsSaving(false);
        inFlight.current = false;
        setLatestBeforeClick(undefined);
      }
    },
    [functionId, mutateAsync, onBeforeRun, refetchLatestTest, testInput],
  );

  useEffect(() => {
    if (!buildId || build?.status !== "Succeeded") return;
    if (autoRanForBuild.current === buildId) return;
    autoRanForBuild.current = buildId;
    void handleRun({ fromBuild: true });
  }, [buildId, build?.status, handleRun]);

  const canRun = !isActive && cooldownSeconds === 0;

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (!(event.metaKey || event.ctrlKey) || event.key !== "Enter") return;
      event.preventDefault();
      if (canRun) void handleRun();
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [canRun, handleRun]);

  // The header's "Test run" button raises a request on the editor store; this is the subscription
  // that answers it, so the button works from any tab without the panel being lifted into the page.
  // It obeys the same rule as the panel button: no second test while one runs or the limit holds.
  const canRunRef = useRef(canRun);
  useEffect(() => {
    canRunRef.current = canRun;
  }, [canRun]);
  useEffect(
    () =>
      useFunctionEditorStore.subscribe((state, previous) => {
        if (state.testRunRequestedAt !== previous.testRunRequestedAt && canRunRef.current) {
          void handleRun();
        }
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

  const currentStep = stepIndex(isSaving, isBuildInFlight, run?.status);
  const failed = !!run && TERMINAL_RUN_STATUSES.includes(run.status) && run.status !== "Succeeded";
  const elapsedSeconds =
    startedAt && isActive ? Math.max(0, Math.round((now - startedAt) / 1000)) : null;
  const meta = [
    run?.durationMs == null ? null : formatDuration(run.durationMs),
    run?.peakMemoryBytes == null ? null : formatMegabytes(run.peakMemoryBytes),
    run?.versionNumber ? `v${run.versionNumber}` : null,
  ]
    .filter(Boolean)
    .join(" · ");
  const explanation = explainRunError(run?.errorCode, run?.errorMessage);
  const result = run && !isRunLive ? prettyJson(run.result) : null;
  const showSteps = isActive || !!run || !!buildId;

  const buttonLabel = isActive
    ? "Running…"
    : cooldownSeconds > 0
      ? `Next test in ${cooldownSeconds}s`
      : runId
        ? "Run again"
        : "Run test";

  return (
    <div className="flex flex-col gap-3">
      <Card className="overflow-hidden">
        <div className="flex items-center justify-between gap-3 border-b px-4 py-3">
          <span className="text-sm font-semibold">Test input</span>
          <span className="text-xs text-low-emphasis">
            sent as{" "}
            <code className="font-mono">{testVerb === "GET" ? "input.query" : "input.body"}</code> ·
            same limits as production
          </span>
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
              onClick={() => lastRun?.input && setTestInput(payloadOf(lastRun.input))}
            >
              Use last run input
            </Button>
          </div>
        </div>
        <div className="flex flex-col gap-2 border-t px-4 py-3">
          <div className="flex gap-2">
            <Button className="flex-1 gap-1.5" disabled={!canRun} onClick={() => void handleRun()}>
              {isActive ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : cooldownSeconds > 0 ? (
                <Clock className="h-4 w-4" />
              ) : (
                <Play className="h-4 w-4" />
              )}
              {buttonLabel}
            </Button>
            {isRunLive && runId && (
              <Button
                variant="outline"
                className="gap-1.5"
                disabled={isCancelling}
                onClick={() => cancelRun(runId)}
              >
                <Square className="h-3.5 w-3.5" />
                Stop
              </Button>
            )}
          </div>
          {requestError && (
            <p role="alert" className="text-xs leading-relaxed text-error">
              {requestError}
            </p>
          )}
          {cooldownSeconds > 0 && (
            <p className="text-xs leading-relaxed text-medium-emphasis">
              One test per function at a time, with a short wait between tests — each test builds
              and runs on a shared runner.
            </p>
          )}
        </div>
      </Card>

      {showSteps && (
        <Card className="overflow-hidden">
          <div className="flex items-center justify-between gap-3 border-b px-4 py-2.5">
            <div className="flex min-w-0 items-center gap-2">
              {run ? (
                <RunStatusChip status={run.status} />
              ) : (
                <span className="text-xs font-semibold text-medium-emphasis">Starting…</span>
              )}
              {elapsedSeconds != null && (
                <span className="text-xs tabular-nums text-medium-emphasis">{elapsedSeconds}s</span>
              )}
              {!isActive && !!meta && (
                <span className="truncate text-xs text-medium-emphasis">{meta}</span>
              )}
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

          <ol className="flex flex-col gap-2.5 border-b px-4 py-3" aria-label="Test progress">
            {STEPS.map((step, index) => {
              const isDone = index < currentStep || (index === 4 && currentStep === 4 && !failed);
              const isCurrent = index === currentStep && currentStep < 4;
              const isFailed = index === 4 && failed;
              const title =
                index === 4 && currentStep === 4 ? (failed ? "Failed" : "Succeeded") : step.title;
              return (
                <li
                  key={step.title}
                  aria-current={isCurrent ? "step" : undefined}
                  className="flex gap-2.5"
                >
                  <span className="mt-0.5 shrink-0">
                    {isFailed ? (
                      <XCircle className="h-4 w-4 text-error" />
                    ) : isCurrent ? (
                      <Loader2 className="h-4 w-4 animate-spin text-primary" />
                    ) : isDone ? (
                      <CheckCircle2 className="h-4 w-4 text-success" />
                    ) : (
                      <Circle className="h-4 w-4 text-low-emphasis" />
                    )}
                  </span>
                  <span className="flex min-w-0 flex-col">
                    <span
                      className={cn(
                        "text-xs",
                        isCurrent && "font-semibold text-primary",
                        isFailed && "font-semibold text-error",
                        isDone && "text-medium-emphasis",
                        !isCurrent && !isDone && !isFailed && "text-low-emphasis",
                      )}
                    >
                      {title}
                    </span>
                    {isCurrent && step.hint && (
                      <span className="text-[11px] leading-relaxed text-low-emphasis">
                        {step.hint}
                      </span>
                    )}
                  </span>
                </li>
              );
            })}
          </ol>

          {buildId && !run && (
            <div className="border-b px-4 py-2.5">
              <BuildProgress buildId={buildId} />
            </div>
          )}

          {(run?.errorCode || run?.errorMessage) && (
            <div className="flex flex-col gap-1.5 border-b bg-error/5 px-4 py-2.5">
              {run?.errorCode && (
                <span className="font-mono text-xs font-semibold text-error">{run.errorCode}</span>
              )}
              {/* Verbatim, wrapped and selectable: the message the runner sent is the thing worth
                  reading, and the hint under it is only a hint. */}
              {run?.errorMessage && (
                <pre className="whitespace-pre-wrap break-words font-mono text-[11px] leading-relaxed text-error">
                  {run.errorMessage}
                </pre>
              )}
              {explanation && (
                <p className="text-xs leading-relaxed text-medium-emphasis">{explanation}</p>
              )}
            </div>
          )}

          {result && (
            <div className="border-b">
              <p className="px-4 pt-2.5 text-xs font-semibold">Returned</p>
              <pre
                aria-label="Returned value"
                className="max-h-52 overflow-auto whitespace-pre-wrap break-words px-4 py-2 font-mono text-[11px] leading-relaxed"
              >
                {result}
              </pre>
            </div>
          )}

          <p className="px-4 pt-2.5 text-xs font-semibold">Logs</p>
          <CardContent className="max-h-52 overflow-auto bg-surface-app p-4 pt-2">
            {(logs?.data ?? []).length === 0 ? (
              <p className="text-xs text-low-emphasis">
                {isActive ? "Logs appear when the run ends." : "No log lines."}
              </p>
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
                    <span className="min-w-0 flex-1 whitespace-pre-wrap break-words">
                      {line.message}
                      {/* ctx.log's structured payload, which was being dropped on the floor. */}
                      {line.data && (
                        <span className="mt-0.5 block whitespace-pre-wrap break-words text-low-emphasis">
                          {line.data}
                        </span>
                      )}
                    </span>
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

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
import { useRunStream } from "../../hooks/use-run-stream";
import { BuildProgress } from "../build-progress";
import { useFunctionEditorStore } from "../../store/function-editor-store";
import { explainRunError } from "../../constants/run-error.constant";
import { TERMINAL_RUN_STATUSES } from "../../types/run.types";
import { formatDuration, formatMegabytes } from "../../utils/format";
import { payloadOf } from "../../utils/test-input";
import { testHttpVerb } from "../../constants/endpoint.constant";
import { useT, tStatic } from "@seliseblocks/genesis-os";
import { renderRichText } from "@/lib/rich-text";

const LOGIC_FUNCTIONS_COMPONENTS_MODULE = "logic.functions.components";

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
  {
    get title() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TEST_PANEL.SAVE_CODE");
    },
    get hint() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TEST_PANEL.SAVING_THE_CODE_ON");
    },
  },
  {
    get title() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TEST_PANEL.WAIT_FOR_A_RUNNER");
    },
    get hint() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TEST_PANEL.THE_TEST_IS_QUEUED");
    },
  },
  {
    get title() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TEST_PANEL.BUILD_AND_START_SANDBOX");
    },
    get hint() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TEST_PANEL.INSTALLING_YOUR_PACKAGES_AND");
    },
  },
  {
    get title() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TEST_PANEL.RUN_HANDLER");
    },
    get hint() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TEST_PANEL.YOUR_HANDLER_IS_RUNNING");
    },
  },
  {
    get title() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TEST_PANEL.RESULT");
    },
    hint: "",
  },
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
  if (!isErrorWithErrors(error))
    return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TEST_PANEL.THE_TEST_COULD_NOT");
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
const StepIcon = ({
  isFailed,
  isCurrent,
  isDone,
}: {
  isFailed: boolean;
  isCurrent: boolean;
  isDone: boolean;
}) => {
  if (isFailed) return <XCircle className="h-4 w-4 text-error" />;
  if (isCurrent) return <Loader2 className="h-4 w-4 animate-spin text-primary" />;
  if (isDone) return <CheckCircle2 className="h-4 w-4 text-success" />;
  return <Circle className="h-4 w-4 text-low-emphasis" />;
};

export const TestPanel = ({ functionId, lastRunId, onOpenRun, onBeforeRun }: TestPanelProps) => {
  const { t } = useT(LOGIC_FUNCTIONS_COMPONENTS_MODULE);
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
  // A handler that streams (`async function*`) is shown piece by piece while it runs.
  const stream = useRunStream(runId, run?.status === "Running");
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
        return setInputError(tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TEST_PANEL.THAT_IS_NOT_VALID"));
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
            ? tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TEST_PANEL.THIS_FUNCTION_WAS_TESTED")
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
      setInputError(t("TEST_PANEL.THAT_IS_NOT_VALID"));
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
  // A streamed answer is shown as it came; the run's result only keeps its start, so it would repeat it.
  const hasStream = stream.text !== "";
  const result = run && !isRunLive && !hasStream ? prettyJson(run.result) : null;
  const showSteps = isActive || !!run || !!buildId;

  let idleLabel = t("TEST_PANEL.RUN_TEST");
  if (cooldownSeconds > 0) idleLabel = t("TEST_PANEL.NEXT_TEST_IN_S", { cooldownSeconds });
  else if (runId) idleLabel = t("TEST_PANEL.RUN_AGAIN");
  const buttonLabel = isActive ? "Running…" : idleLabel;

  return (
    <div className="flex flex-col gap-3">
      <Card className="overflow-hidden">
        <div className="flex items-center justify-between gap-3 border-b px-4 py-3">
          <span className="text-sm font-semibold">{t("TEST_PANEL.TEST_INPUT")}</span>
          <span className="text-xs text-low-emphasis">
            {renderRichText(
              t(testVerb === "GET" ? "TEST_PANEL.SENT_AS_QUERY" : "TEST_PANEL.SENT_AS_BODY"),
              [(text) => <code className="font-mono">{text}</code>],
            )}
          </span>
        </div>
        <Textarea
          aria-label={t("TEST_PANEL.TEST_INPUT_JSON")}
          spellCheck={false}
          className="min-h-[150px] resize-y rounded-none border-0 font-mono text-xs focus-visible:ring-0 focus-visible:ring-offset-0"
          value={testInput}
          onChange={(e) => setTestInput(e.target.value)}
        />
        {inputError && <p className="px-4 pb-2 text-xs text-error">{inputError}</p>}
        <div className="flex items-center justify-between gap-2 border-t px-4 py-2">
          <div className="flex items-center gap-1">
            <Button variant="ghost" size="xs" className="px-2 text-xs" onClick={formatInput}>
              {t("TEST_PANEL.FORMAT")}
            </Button>
            <Button
              variant="ghost"
              size="xs"
              className="px-2 text-xs"
              disabled={!lastRun?.input}
              onClick={() => lastRun?.input && setTestInput(payloadOf(lastRun.input))}
            >
              {t("TEST_PANEL.USE_LAST_RUN_INPUT")}
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
                {t("TEST_PANEL.STOP")}
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
              {t("TEST_PANEL.ONE_TEST_PER_FUNCTION")}
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
                <span className="text-xs font-semibold text-medium-emphasis">
                  {t("TEST_PANEL.STARTING")}
                </span>
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
                {t("TEST_PANEL.OPEN_RUN")}
              </button>
            )}
          </div>

          <ol
            className="flex flex-col gap-2.5 border-b px-4 py-3"
            aria-label={t("TEST_PANEL.TEST_PROGRESS")}
          >
            {STEPS.map((step, index) => {
              const isDone = index < currentStep || (index === 4 && currentStep === 4 && !failed);
              const isCurrent = index === currentStep && currentStep < 4;
              const isFailed = index === 4 && failed;
              const finished = index === 4 && currentStep === 4;
              const finalTitle = failed ? "Failed" : "Succeeded";
              const title = finished ? finalTitle : step.title;
              return (
                <li
                  key={step.title}
                  aria-current={isCurrent ? "step" : undefined}
                  className="flex gap-2.5"
                >
                  <span className="mt-0.5 shrink-0">
                    <StepIcon isFailed={isFailed} isCurrent={isCurrent} isDone={isDone} />
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

          {hasStream && (
            <div className="border-b">
              <p className="flex items-center gap-1.5 px-4 pt-2.5 text-xs font-semibold">
                {t("TEST_PANEL.STREAMED_ANSWER")}
                {stream.state === "streaming" && (
                  <Loader2
                    aria-label={t("TEST_PANEL.STREAMING")}
                    className="h-3 w-3 animate-spin text-primary"
                  />
                )}
              </p>
              <pre
                aria-label={t("TEST_PANEL.STREAMED_ANSWER")}
                aria-live="polite"
                className="max-h-52 overflow-auto whitespace-pre-wrap break-words px-4 py-2 font-mono text-[11px] leading-relaxed"
              >
                {stream.text}
              </pre>
              {stream.clipped && (
                <p className="px-4 pb-2 text-[11px] text-low-emphasis">
                  {t("TEST_PANEL.ONLY_THE_FIRST_1")}
                </p>
              )}
              {stream.error && (
                <p role="alert" className="px-4 pb-2 text-[11px] text-error">
                  {t("TEST_PANEL.THE_STREAM_STOPPED", { error: stream.error })}
                </p>
              )}
            </div>
          )}

          {result && (
            <div className="border-b">
              <p className="px-4 pt-2.5 text-xs font-semibold">{t("TEST_PANEL.RETURNED")}</p>
              <pre
                aria-label={t("TEST_PANEL.RETURNED_VALUE")}
                className="max-h-52 overflow-auto whitespace-pre-wrap break-words px-4 py-2 font-mono text-[11px] leading-relaxed"
              >
                {result}
              </pre>
            </div>
          )}

          <p className="px-4 pt-2.5 text-xs font-semibold">{t("TEST_PANEL.LOGS")}</p>
          <CardContent className="max-h-52 overflow-auto bg-surface-app p-4 pt-2">
            {(logs?.data ?? []).length === 0 ? (
              <p className="text-xs text-low-emphasis">
                {isActive ? t("TEST_PANEL.LOGS_APPEAR_WHEN_THE") : t("TEST_PANEL.NO_LOG_LINES")}
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

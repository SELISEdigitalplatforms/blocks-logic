"use client";
import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate, useParams } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";
import { Check, Copy, Loader2, Pencil, Rocket, Save } from "lucide-react";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { Input } from "@/components/ui-kits/input/input";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { cn } from "@/lib/utils";
import { useGetFunction } from "../../hooks/use-function";
import { useFunctionEditor } from "../../hooks/use-function-editor";
import { useDeployFunction, useUpdateFunction } from "../../hooks/use-functions";
import { useGetRuns } from "../../hooks/use-runs";
import { useGetVersions } from "../../hooks/use-versions";
import { useFunctionEditorStore } from "../../store/function-editor-store";
import { TERMINAL_RUN_STATUSES } from "../../types/run.types";
import { FunctionStatusChip } from "../../components/function-status-chip";
import { CodeEditor } from "../../components/code-editor";
import { SandboxHelpCard } from "../../components/sandbox-help-card";
import { EnvironmentCard } from "../../components/environment-card";
import { TestPanel } from "../../components/test-panel";
import { LimitsForm } from "../../components/limits-form";
import { RetryForm } from "../../components/retry-form";
import { VariablesEditor } from "../../components/variables-editor";
import { DangerZoneCard } from "../../components/danger-zone-card";
import { TriggerHttpCard } from "../../components/trigger-http-card";
import { TriggerWorkflowCard } from "../../components/trigger-workflow-card";
import { InvokeSnippetCard } from "../../components/invoke-snippet-card";
import { OutputActionsEditor } from "../../components/output-actions-editor";
import { RunsTable } from "../../components/runs-table";
import { RunsFilterBar, RunsFilterValue } from "../../components/runs-filter-bar";
import { RunDetail } from "../../components/run-detail";
import { VersionsTable } from "../../components/versions-table";
import { DeleteFunctionDialog } from "../../components/delete-function-dialog";
import { buildInvokeUrl } from "../../components/endpoint-badge";

const RUNS_PAGE_SIZE = 20;

/**
 * The runs range chips, as an ISO lower bound for the query.
 *
 * Rounded down to the minute, and it must stay that way: this value is part of the runs query key,
 * so a fresh `Date.now()` on every render gave every render a new key — react-query fetched, the
 * result re-rendered, the key changed again. That is the request-per-render loop that filled the
 * network panel and kept the Runs tab on its skeleton forever.
 */
const rangeStart = (range: string, now: number): string => {
  const hours = range === "24h" ? 24 : range === "30d" ? 24 * 30 : 24 * 7;
  const start = now - hours * 60 * 60 * 1000;
  return new Date(Math.floor(start / 60_000) * 60_000).toISOString();
};

const TAB_ORDER = ["code", "trigger", "output", "configuration", "runs", "versions"] as const;
const TAB_LABELS: Record<(typeof TAB_ORDER)[number], string> = {
  code: "Code",
  trigger: "Trigger",
  output: "Output",
  configuration: "Configuration",
  runs: "Runs",
  versions: "Versions",
};

export const FunctionDetailPage = () => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { pathname } = useLocation();
  const params = useParams<{ itemId: string; functionId: string }>();
  const functionId = params.functionId ?? "";

  const [queryParams, setQueryParams] = useQueryStates({
    tab: parseAsString.withDefault("code"),
    runId: parseAsString.withDefault(""),
    runStatus: parseAsString.withDefault(""),
    runTrigger: parseAsString.withDefault(""),
    runRange: parseAsString.withDefault("7d"),
    runSearch: parseAsString.withDefault(""),
    runPage: parseAsInteger.withDefault(0),
  });
  const [isRunsAutoRefresh, setIsRunsAutoRefresh] = useState(true);
  const [isDeleteOpen, setIsDeleteOpen] = useState(false);
  const [renameDraft, setRenameDraft] = useState<string | null>(null);
  const [isEndpointCopied, setIsEndpointCopied] = useState(false);
  const renameInputRef = useRef<HTMLInputElement>(null);
  const isRenameSubmitting = useRef(false);

  const { data: fn, isLoading, isFetched } = useGetFunction({ functionId });
  const { save, isSaving, isDirty } = useFunctionEditor(functionId, fn);
  const { mutateAsync: deployAsync, isPending: isDeploying } = useDeployFunction();
  const { mutateAsync: renameAsync, isPending: isRenaming } = useUpdateFunction();

  const indexJs = useFunctionEditorStore((s) => s.indexJs);
  const packageJson = useFunctionEditorStore((s) => s.packageJson);
  const setIndexJs = useFunctionEditorStore((s) => s.setIndexJs);
  const setPackageJson = useFunctionEditorStore((s) => s.setPackageJson);
  const activeFile = useFunctionEditorStore((s) => s.activeFile);
  const setActiveFile = useFunctionEditorStore((s) => s.setActiveFile);
  const limits = useFunctionEditorStore((s) => s.limits);
  const setLimits = useFunctionEditorStore((s) => s.setLimits);
  const retry = useFunctionEditorStore((s) => s.retry);
  const setRetry = useFunctionEditorStore((s) => s.setRetry);
  const trigger = useFunctionEditorStore((s) => s.trigger);
  const setTrigger = useFunctionEditorStore((s) => s.setTrigger);
  const outputActions = useFunctionEditorStore((s) => s.outputActions);
  const setOutputActions = useFunctionEditorStore((s) => s.setOutputActions);
  const variables = useFunctionEditorStore((s) => s.variables);
  const requestTestRun = useFunctionEditorStore((s) => s.requestTestRun);
  const setTestInput = useFunctionEditorStore((s) => s.setTestInput);
  const setVariables = useFunctionEditorStore((s) => s.setVariables);

  // Held in state and written from an effect: reading the clock during render is the very thing
  // that made this value unstable, and `react-hooks/purity` is right to reject it. Recomputed when
  // the range changes, then once a minute so a page left open does not keep a stale window.
  const [runsFromUtc, setRunsFromUtc] = useState("");
  useEffect(() => {
    const apply = () => setRunsFromUtc(rangeStart(queryParams.runRange, Date.now()));
    apply();
    const timer = window.setInterval(apply, 60_000);
    return () => window.clearInterval(timer);
  }, [queryParams.runRange]);

  const runsFilter: RunsFilterValue = {
    status: queryParams.runStatus,
    invokedBy: queryParams.runTrigger,
    range: queryParams.runRange,
    search: queryParams.runSearch,
    autoRefresh: isRunsAutoRefresh,
  };
  const { data: runsData, isLoading: isRunsLoading } = useGetRuns(
    {
      functionId,
      // Every chip, "Running" included, is a server-side filter: the API maps it to the
      // non-terminal set. Trimming the page client-side made the total and the pager describe a
      // different set of runs than the table showed.
      status: queryParams.runStatus || undefined,
      invokedBy: queryParams.runTrigger || undefined,
      searchKey: queryParams.runSearch || undefined,
      fromUtc: runsFromUtc,
      pageNumber: queryParams.runPage,
      pageSize: RUNS_PAGE_SIZE,
    },
    { autoRefresh: isRunsAutoRefresh, enabled: !!runsFromUtc },
  );
  const { data: versionsData, isLoading: isVersionsLoading } = useGetVersions(functionId);

  useEffect(() => {
    if (functionId && isFetched && !isLoading && !fn) {
      showErrorToast({ errors: "Function not found" });
      navigate(scoped("functions"));
    }
  }, [functionId, isFetched, isLoading, fn, navigate, scoped]);

  useEffect(() => {
    if (renameDraft !== null) renameInputRef.current?.focus();
  }, [renameDraft]);

  const handleDeploy = async () => {
    try {
      // Deploying the previous source because the save failed would ship the wrong code.
      if (isDirty && !(await save())) return;
      const version = await deployAsync({ functionId });
      showSuccessToast({ description: `Deployed as v${version.number}.` });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to deploy function" });
    }
  };

  const handleRename = async () => {
    // Enter and blur both land here, and they fire in sequence for a single rename: submitting
    // sets `isRenaming`, which disables the input, and disabling a focused field blurs it. A ref
    // rather than `isRenaming` because the guard has to hold within one render, before React has
    // re-rendered this handler with the new flag.
    if (isRenameSubmitting.current) return;
    const name = renameDraft?.trim();
    if (!fn || !name || name === fn.name) return setRenameDraft(null);
    isRenameSubmitting.current = true;
    try {
      await renameAsync({ functionId, name, description: fn.description });
      setRenameDraft(null);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to rename function" });
    } finally {
      isRenameSubmitting.current = false;
    }
  };

  const copyEndpoint = () => {
    navigator.clipboard.writeText(buildInvokeUrl(functionId));
    setIsEndpointCopied(true);
    setTimeout(() => setIsEndpointCopied(false), 1400);
  };

  if (isLoading || !isFetched) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    );
  }
  if (!fn) return null;

  // Deploy answers with the version once the image is built, so "Building…" is exactly the window
  // where that request is in flight — the API exposes no build id to poll from here.
  const isBuilding = isDeploying;
  const isDeployed = fn.activeVersionNumber != null;
  const hasUndeployedChanges = isDirty || fn.isDirty;
  const deployLabel = isBuilding
    ? "Building…"
    : !isDeployed
      ? "Deploy"
      : hasUndeployedChanges
        ? `Deploy v${fn.lastVersionNumber + 1}`
        : "Up to date ✓";

  const runs = runsData?.data ?? [];
  const hasActiveRun = runs.some((run) => !TERMINAL_RUN_STATUSES.includes(run.status));
  const hasRunFilters =
    !!queryParams.runStatus || !!queryParams.runTrigger || !!queryParams.runSearch;

  const tabBadges: Record<(typeof TAB_ORDER)[number], string> = {
    code: "",
    trigger: "",
    output: outputActions.length ? String(outputActions.length) : "",
    configuration: variables.length ? String(variables.length) : "",
    runs: runsData?.totalCount ? String(runsData.totalCount) : "",
    versions: isDeployed ? `v${fn.activeVersionNumber}` : "",
  };

  return (
    <div className="flex min-h-screen flex-col">
      <div className="px-6 pt-4 pb-2">
        {/* The last crumb is the function id, which prettifies into nonsense — name it explicitly. */}
        <PageBreadcrumb breadcrumbIndex={3} customTitles={{ [pathname]: fn.name }} />
      </div>

      <div className="flex-1 space-y-4 px-6 pb-8">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex min-w-0 flex-col gap-1.5">
            <div className="flex items-center gap-2.5">
              {renameDraft === null ? (
                <>
                  <h1 className="truncate text-2xl font-bold tracking-tight">{fn.name}</h1>
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label="Rename function"
                    className="h-7 w-7 text-medium-emphasis hover:text-foreground"
                    onClick={() => setRenameDraft(fn.name)}
                  >
                    <Pencil className="h-3.5 w-3.5" />
                  </Button>
                </>
              ) : (
                <Input
                  ref={renameInputRef}
                  aria-label="Function name"
                  className="h-9 max-w-xs text-lg font-semibold"
                  value={renameDraft}
                  disabled={isRenaming}
                  onChange={(e) => setRenameDraft(e.target.value)}
                  onBlur={() => void handleRename()}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") void handleRename();
                    if (e.key === "Escape") setRenameDraft(null);
                  }}
                />
              )}
              <FunctionStatusChip status={fn.status} />
            </div>

            <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-medium-emphasis">
              <span className="flex min-w-0 items-center gap-1.5">
                <code className="truncate font-mono">POST {buildInvokeUrl(functionId)}</code>
                <button
                  type="button"
                  aria-label="Copy endpoint"
                  className="shrink-0 text-medium-emphasis hover:text-foreground"
                  onClick={copyEndpoint}
                >
                  {isEndpointCopied ? (
                    <Check className="h-3.5 w-3.5 text-success" />
                  ) : (
                    <Copy className="h-3.5 w-3.5" />
                  )}
                </button>
              </span>
              <span>
                {isDeployed ? (
                  <>
                    active{" "}
                    <code className="font-mono font-semibold text-primary">
                      v{fn.activeVersionNumber}
                    </code>
                  </>
                ) : (
                  "not deployed"
                )}
              </span>
              {isDirty && (
                <span className="flex items-center gap-1.5 font-medium text-warning-800">
                  <span className="h-1.5 w-1.5 rounded-full bg-warning-800" />
                  unsaved changes
                </span>
              )}
            </div>
          </div>

          <div className="flex shrink-0 items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                setQueryParams({ tab: "code", runId: "" });
                requestTestRun();
              }}
            >
              Test run
            </Button>
            <Button
              variant="outline"
              size="sm"
              className="gap-1.5"
              onClick={() => void save()}
              disabled={isSaving || !isDirty}
            >
              <Save className="h-3.5 w-3.5" />
              {isSaving ? "Saving…" : "Save"}
            </Button>
            <Button
              size="sm"
              className="gap-1.5"
              onClick={handleDeploy}
              disabled={isBuilding || (isDeployed && !hasUndeployedChanges)}
            >
              {isBuilding ? (
                <Loader2 className="h-3.5 w-3.5 animate-spin" />
              ) : (
                <Rocket className="h-3.5 w-3.5" />
              )}
              {deployLabel}
            </Button>
          </div>
        </div>

        <Tabs
          value={queryParams.tab}
          // Leaving the runs tab clears its filters. They only mean anything beside the runs
          // table, and a URL carrying `?runStatus=Running` while another tab is open both reads
          // as nonsense and silently narrows the list on the next visit.
          onValueChange={(tab) =>
            setQueryParams(
              tab === "runs"
                ? { tab }
                : {
                    tab,
                    runId: null,
                    runStatus: null,
                    runTrigger: null,
                    runSearch: null,
                    runRange: null,
                    runPage: null,
                  },
            )
          }
          className="flex flex-col gap-3"
        >
          {/* Segmented pills, matching Blocks OS settings (idp/settings/pages/settings-page).
              Six tabs do not fit a phone, so the strip scrolls rather than wrapping — the
              pill group keeps its shape instead of breaking onto a second row. */}
          <div className="-mx-1 overflow-x-auto px-1 pb-0.5">
            <TabsList className="h-[42px] w-max bg-blocks-primary-shades-300">
              {TAB_ORDER.map((tab) => (
                <TabsTrigger key={tab} value={tab} className="h-8 gap-2 px-4 text-sm font-medium">
                  {TAB_LABELS[tab]}
                  {!!tabBadges[tab] && (
                    <span className="text-[10px] font-semibold text-low-emphasis">
                      {tabBadges[tab]}
                    </span>
                  )}
                </TabsTrigger>
              ))}
            </TabsList>
          </div>

          <TabsContent
            value="code"
            className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_340px]"
          >
            <Card className="min-w-0 overflow-hidden">
              <div className="flex items-center justify-between gap-3 border-b bg-surface-app pr-4">
                <div className="flex">
                  {(["index.js", "package.json"] as const).map((file) => (
                    <button
                      key={file}
                      className={cn(
                        "border-b-2 px-4 py-2.5 font-mono text-xs",
                        activeFile === file
                          ? "border-primary font-semibold text-primary"
                          : "border-transparent text-medium-emphasis hover:text-foreground",
                      )}
                      onClick={() => setActiveFile(file)}
                    >
                      {file}
                    </button>
                  ))}
                </div>
                <span className="text-xs text-low-emphasis">
                  {activeFile === "index.js"
                    ? `ES modules · ${indexJs.split("\n").length} lines`
                    : "pinned versions only · installed at deploy"}
                </span>
              </div>
              {activeFile === "index.js" ? (
                <CodeEditor
                  language="javascript"
                  value={indexJs}
                  onChange={setIndexJs}
                  height="clamp(280px, 46vh, 520px)"
                  className="overflow-hidden"
                  // The tenant's own keys, so `ctx.env.` completes with what is actually bound.
                  envKeys={variables.map((variable) => variable.key)}
                />
              ) : (
                <CodeEditor
                  language="json"
                  value={packageJson}
                  onChange={setPackageJson}
                  height="clamp(280px, 46vh, 520px)"
                  className="overflow-hidden"
                />
              )}
              <p className="border-t bg-surface-app px-4 py-2.5 text-xs text-medium-emphasis">
                Native <code className="font-mono">fetch()</code>, async/await and pinned npm
                packages. Variables arrive as <code className="font-mono">ctx.env.NAME</code>.
              </p>
            </Card>

            <div className="flex min-w-0 flex-col gap-3">
              <TestPanel
                functionId={functionId}
                lastRunId={runsData?.data?.[0]?.id}
                onOpenRun={(runId) => setQueryParams({ tab: "runs", runId })}
                onBeforeRun={isDirty ? save : undefined}
              />
              <EnvironmentCard
                variables={variables}
                onEditVariables={() => setQueryParams({ tab: "configuration" })}
              />
              <SandboxHelpCard limits={limits} />
            </div>
          </TabsContent>

          {/* Full width with the supporting cards in columns, like proxy-details' overview. */}
          <TabsContent value="trigger" className="flex flex-col gap-4">
            <TriggerHttpCard value={trigger} onChange={setTrigger} functionId={fn.id} />
            <div className="grid gap-4 xl:grid-cols-2">
              <TriggerWorkflowCard value={trigger} onChange={setTrigger} />
              <InvokeSnippetCard functionId={fn.id} trigger={trigger} />
            </div>
          </TabsContent>

          <TabsContent value="output" className="flex flex-col gap-4">
            <Card>
              <CardContent>
                <OutputActionsEditor value={outputActions} onChange={setOutputActions} />
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="configuration" className="flex flex-col gap-4">
            <Card className="overflow-hidden">
              <VariablesEditor value={variables} onChange={setVariables} />
            </Card>
            <div className="grid items-start gap-4 xl:grid-cols-2">
              <Card>
                <CardContent className="flex flex-col gap-4 p-5">
                  <div className="flex flex-col gap-1">
                    <span className="text-base font-semibold">Limits</span>
                    <span className="text-xs leading-relaxed text-medium-emphasis">
                      Applied to every invocation. Runs over the concurrency setting queue rather
                      than fail.
                    </span>
                  </div>
                  <LimitsForm value={limits} onChange={setLimits} />
                </CardContent>
              </Card>
              <Card>
                <CardContent className="flex flex-col gap-4 p-5">
                  <div className="flex flex-col gap-1">
                    <span className="text-base font-semibold">Retries</span>
                    <span className="text-xs leading-relaxed text-medium-emphasis">
                      One policy for the whole function — a failed run and a failed output action
                      retry the same way. After the last attempt the run is kept as failed and can
                      be replayed.
                    </span>
                  </div>
                  <RetryForm value={retry} onChange={setRetry} />
                </CardContent>
              </Card>
            </div>
            <DangerZoneCard onDelete={() => setIsDeleteOpen(true)} />
          </TabsContent>

          <TabsContent value="runs" className="flex flex-col gap-4">
            {queryParams.runId ? (
              <>
                <Button
                  variant="ghost"
                  size="sm"
                  className="w-fit px-2 text-primary hover:text-primary"
                  onClick={() => setQueryParams({ runId: "" })}
                >
                  ‹ All runs
                </Button>
                <RunDetail
                  runId={queryParams.runId}
                  memoryLimitMb={limits.memoryMb}
                  onUseAsTestInput={(input) => {
                    setTestInput(input);
                    setQueryParams({ tab: "code", runId: "" });
                  }}
                />
              </>
            ) : (
              <>
                <Card>
                  <CardHeader className="mb-4 p-0">
                    <RunsFilterBar
                      value={runsFilter}
                      hasActiveRun={hasActiveRun}
                      onChange={(partial) => {
                        if (partial.autoRefresh !== undefined)
                          setIsRunsAutoRefresh(partial.autoRefresh);
                        setQueryParams({
                          ...(partial.status !== undefined ? { runStatus: partial.status } : {}),
                          ...(partial.invokedBy !== undefined
                            ? { runTrigger: partial.invokedBy }
                            : {}),
                          ...(partial.range !== undefined ? { runRange: partial.range } : {}),
                          ...(partial.search !== undefined ? { runSearch: partial.search } : {}),
                          runPage: 0,
                        });
                      }}
                    />
                  </CardHeader>
                  <CardContent>
                    <RunsTable
                      runs={runs}
                      isLoading={isRunsLoading}
                      memoryLimitMb={limits.memoryMb}
                      hasFilters={hasRunFilters}
                      isDeployed={isDeployed}
                      onOpenRun={(runId) => setQueryParams({ runId })}
                    />
                    {!!runsData?.totalCount && runsData.totalCount > RUNS_PAGE_SIZE && (
                      <div className="mt-5 flex justify-end">
                        <Pagination
                          totalCount={runsData.totalCount}
                          page={queryParams.runPage}
                          pageSize={RUNS_PAGE_SIZE}
                          pageSizeOptions={[RUNS_PAGE_SIZE]}
                          onChange={(runPage) => setQueryParams({ runPage })}
                          onPageSizeChange={() => undefined}
                        />
                      </div>
                    )}
                  </CardContent>
                </Card>
              </>
            )}
          </TabsContent>

          <TabsContent value="versions" className="flex flex-col gap-4">
            <Card>
              <CardContent>
                <VersionsTable
                  functionId={functionId}
                  versions={versionsData?.data ?? []}
                  activeVersionNumber={fn.activeVersionNumber}
                  isLoading={isVersionsLoading}
                />
              </CardContent>
            </Card>
          </TabsContent>
        </Tabs>
      </div>

      <DeleteFunctionDialog
        open={isDeleteOpen}
        onOpenChange={setIsDeleteOpen}
        fn={{ id: fn.id, name: fn.name }}
        onDeleted={() => navigate(scoped("functions"))}
      />
    </div>
  );
};

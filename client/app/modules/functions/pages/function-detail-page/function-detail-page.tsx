"use client";
import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate, useParams } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";
import {
  Check,
  Copy,
  Loader2,
  MoreHorizontal,
  Pencil,
  Rocket,
  Save,
  Trash2,
} from "lucide-react";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { Input } from "@/components/ui-kits/input/input";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
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

/** The runs range chips, as an ISO lower bound for the query. */
const rangeStart = (range: string): string | undefined => {
  const hours = range === "24h" ? 24 : range === "30d" ? 24 * 30 : 24 * 7;
  return new Date(Date.now() - hours * 60 * 60 * 1000).toISOString();
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

  const runsFilter: RunsFilterValue = {
    status: queryParams.runStatus,
    invokedBy: queryParams.runTrigger,
    range: queryParams.runRange,
    search: queryParams.runSearch,
    autoRefresh: isRunsAutoRefresh,
  };
  const { data: runsData, isLoading: isRunsLoading } = useGetRuns({
    functionId,
    // "Running" in the design covers every non-terminal status; the API filters one status at a
    // time, so that chip is applied to the returned page instead of the query.
    status: queryParams.runStatus === "Running" ? undefined : queryParams.runStatus || undefined,
    invokedBy: queryParams.runTrigger || undefined,
    searchKey: queryParams.runSearch || undefined,
    fromUtc: rangeStart(queryParams.runRange),
    pageNumber: queryParams.runPage,
    pageSize: RUNS_PAGE_SIZE,
  }, { autoRefresh: isRunsAutoRefresh });
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
      if (isDirty) await save();
      const version = await deployAsync({ functionId });
      showSuccessToast({ description: `Deployed as v${version.number}.` });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to deploy function" });
    }
  };

  const handleRename = async () => {
    const name = renameDraft?.trim();
    if (!fn || !name || name === fn.name) return setRenameDraft(null);
    try {
      await renameAsync({ functionId, name, description: fn.description });
      setRenameDraft(null);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to rename function" });
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
  const visibleRuns =
    queryParams.runStatus === "Running"
      ? runs.filter((run) => !TERMINAL_RUN_STATUSES.includes(run.status))
      : runs;
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
                    active <code className="font-mono font-semibold text-primary">v{fn.activeVersionNumber}</code>
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
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="outline" size="icon" aria-label="More actions" className="h-9 w-9">
                  <MoreHorizontal className="h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem className="text-error" onClick={() => setIsDeleteOpen(true)}>
                  <Trash2 className="mr-2 h-3.5 w-3.5" />
                  Delete
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>

        <Tabs
          value={queryParams.tab}
          onValueChange={(tab) => setQueryParams({ tab })}
          className="flex flex-col gap-4"
        >
          <TabsList className="h-auto w-full justify-start gap-0.5 rounded-none border-b bg-transparent p-0">
            {TAB_ORDER.map((tab) => (
              <TabsTrigger
                key={tab}
                value={tab}
                className="gap-2 rounded-none border-b-2 border-transparent px-4 py-2.5 text-medium-emphasis data-[state=active]:border-primary data-[state=active]:bg-transparent data-[state=active]:font-semibold data-[state=active]:text-primary data-[state=active]:shadow-none"
              >
                {TAB_LABELS[tab]}
                {!!tabBadges[tab] && (
                  <span className="text-[10px] font-semibold text-low-emphasis">
                    {tabBadges[tab]}
                  </span>
                )}
              </TabsTrigger>
            ))}
          </TabsList>

          <TabsContent value="code" className="flex flex-wrap items-start gap-4">
            <Card className="min-w-0 flex-1 basis-[560px] overflow-hidden">
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
                  height="560px"
                  className="overflow-hidden"
                  // The tenant's own keys, so `ctx.env.` completes with what is actually bound.
                  envKeys={variables.map((variable) => variable.key)}
                />
              ) : (
                <CodeEditor
                  language="json"
                  value={packageJson}
                  onChange={setPackageJson}
                  height="560px"
                  className="overflow-hidden"
                />
              )}
              <p className="border-t bg-surface-app px-4 py-2.5 text-xs text-medium-emphasis">
                Native <code className="font-mono">fetch()</code>, async/await and pinned npm
                packages. Variables arrive as <code className="font-mono">ctx.env.NAME</code>.
              </p>
            </Card>

            <div className="flex w-full min-w-[280px] flex-col gap-3 lg:w-[330px] lg:flex-none">
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

          <TabsContent value="trigger" className="flex max-w-[780px] flex-col gap-4">
            <TriggerHttpCard value={trigger} onChange={setTrigger} functionId={fn.id} />
            <TriggerWorkflowCard value={trigger} onChange={setTrigger} />
            <InvokeSnippetCard functionId={fn.id} trigger={trigger} />
          </TabsContent>

          <TabsContent value="output" className="max-w-[820px]">
            <OutputActionsEditor value={outputActions} onChange={setOutputActions} />
          </TabsContent>

          <TabsContent value="configuration" className="flex max-w-[820px] flex-col gap-4">
            <Card className="overflow-hidden">
              <VariablesEditor value={variables} onChange={setVariables} />
            </Card>
            <Card>
              <CardContent className="flex flex-col gap-4 p-5">
                <div className="flex flex-col gap-1">
                  <span className="text-base font-semibold">Limits</span>
                  <span className="text-xs leading-relaxed text-medium-emphasis">
                    Applied to every invocation. Runs over the concurrency setting queue rather than
                    fail.
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
                    One policy for the whole function — a failed run and a failed output action retry
                    the same way. After the last attempt the run is kept as failed and can be
                    replayed.
                  </span>
                </div>
                <RetryForm value={retry} onChange={setRetry} />
              </CardContent>
            </Card>
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
                <RunsFilterBar
                  value={runsFilter}
                  hasActiveRun={hasActiveRun}
                  onChange={(partial) => {
                    if (partial.autoRefresh !== undefined) setIsRunsAutoRefresh(partial.autoRefresh);
                    setQueryParams({
                      ...(partial.status !== undefined ? { runStatus: partial.status } : {}),
                      ...(partial.invokedBy !== undefined ? { runTrigger: partial.invokedBy } : {}),
                      ...(partial.range !== undefined ? { runRange: partial.range } : {}),
                      ...(partial.search !== undefined ? { runSearch: partial.search } : {}),
                      runPage: 0,
                    });
                  }}
                />
                <RunsTable
                  runs={visibleRuns}
                  isLoading={isRunsLoading}
                  memoryLimitMb={limits.memoryMb}
                  hasFilters={hasRunFilters}
                  isDeployed={isDeployed}
                  onOpenRun={(runId) => setQueryParams({ runId })}
                />
                {!!runsData?.totalCount && runsData.totalCount > RUNS_PAGE_SIZE && (
                  <div className="flex justify-end">
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
              </>
            )}
          </TabsContent>

          <TabsContent value="versions">
            <VersionsTable
              functionId={functionId}
              versions={versionsData?.data ?? []}
              activeVersionNumber={fn.activeVersionNumber}
              isLoading={isVersionsLoading}
            />
          </TabsContent>
        </Tabs>
      </div>

      <DeleteFunctionDialog
        open={isDeleteOpen}
        onOpenChange={setIsDeleteOpen}
        fn={{
          id: fn.id,
          name: fn.name,
          status: fn.status,
          isDirty: fn.isDirty,
          activeVersionNumber: fn.activeVersionNumber,
          totalRuns: runsData?.totalCount ?? 0,
          runs24h: 0,
          httpEnabled: trigger.httpEnabled,
          workflowEnabled: trigger.workflowEnabled,
          lastUpdatedDate: "",
        }}
        onDeleted={() => navigate(scoped("functions"))}
      />
    </div>
  );
};

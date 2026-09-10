"use client";
import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { parseAsString, useQueryStates } from "nuqs";
import {
  Loader2,
  Trash2,
  Save,
  Rocket,
  Code2,
  Settings,
  Webhook,
  Send,
  History,
  GitBranch,
} from "lucide-react";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useGetFunction } from "../../hooks/use-function";
import { useFunctionEditor } from "../../hooks/use-function-editor";
import { useDeployFunction } from "../../hooks/use-functions";
import { useGetRuns } from "../../hooks/use-runs";
import { useGetVersions } from "../../hooks/use-versions";
import { useFunctionEditorStore } from "../../store/function-editor-store";
import { FunctionStatusChip } from "../../components/function-status-chip";
import { CodeEditor } from "../../components/code-editor";
import { SandboxHelpCard } from "../../components/sandbox-help-card";
import { TestPanel } from "../../components/test-panel";
import { LimitsForm } from "../../components/limits-form";
import { RetryForm } from "../../components/retry-form";
import { VariablesEditor } from "../../components/variables-editor";
import { TriggerHttpCard } from "../../components/trigger-http-card";
import { TriggerWorkflowCard } from "../../components/trigger-workflow-card";
import { InvokeSnippetCard } from "../../components/invoke-snippet-card";
import { OutputActionsEditor } from "../../components/output-actions-editor";
import { RunsTable } from "../../components/runs-table";
import { RunDetail } from "../../components/run-detail";
import { VersionsTable } from "../../components/versions-table";
import { DeleteFunctionDialog } from "../../components/delete-function-dialog";

const TABS = [
  { value: "code", label: "Code", icon: Code2 },
  { value: "configuration", label: "Configuration", icon: Settings },
  { value: "trigger", label: "Trigger", icon: Webhook },
  { value: "output", label: "Output", icon: Send },
  { value: "runs", label: "Runs", icon: History },
  { value: "versions", label: "Versions", icon: GitBranch },
] as const;

export const FunctionDetailPage = () => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const params = useParams<{ itemId: string; functionId: string }>();
  const functionId = params.functionId ?? "";

  const [queryParams, setQueryParams] = useQueryStates({
    tab: parseAsString.withDefault("code"),
    runId: parseAsString.withDefault(""),
  });
  const [isDeleteOpen, setIsDeleteOpen] = useState(false);

  const { data: fn, isLoading, isFetched } = useGetFunction({ functionId });
  const { save, isSaving, isDirty } = useFunctionEditor(functionId, fn);
  const { mutateAsync: deployAsync, isPending: isDeploying } = useDeployFunction();

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
  const setVariables = useFunctionEditorStore((s) => s.setVariables);

  const { data: runsData, isLoading: isRunsLoading } = useGetRuns({
    functionId,
    pageNumber: 0,
    pageSize: 20,
  });
  const { data: versionsData, isLoading: isVersionsLoading } = useGetVersions(functionId);

  useEffect(() => {
    if (functionId && isFetched && !isLoading && !fn) {
      showErrorToast({ errors: "Function not found" });
      navigate(scoped("functions"));
    } else if (fn?.name && functionId) {
      BREADCRUMB_CUSTOM_TITLES[`/functions/${functionId}`] = fn.name;
    }
  }, [functionId, isFetched, isLoading, fn, navigate, scoped]);

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

  if (isLoading || !isFetched) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    );
  }
  if (!fn) return null;

  return (
    <div className="flex min-h-screen flex-col">
      <div className="px-6 pt-4 pb-2">
        <PageBreadcrumb breadcrumbIndex={3} />
      </div>

      <div className="flex-1 space-y-6 px-6 pb-8">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <div className="flex items-center gap-2.5">
              <h1 className="text-2xl font-bold tracking-tight">{fn.name}</h1>
              <FunctionStatusChip status={fn.status} />
              {isDirty && (
                <span className="text-xs font-medium text-muted-foreground">Unsaved changes</span>
              )}
            </div>
            <p className="mt-1 font-mono text-xs text-muted-foreground">{fn.id}</p>
          </div>

          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              className="gap-1.5 text-error hover:bg-destructive/10 hover:text-error"
              onClick={() => setIsDeleteOpen(true)}
            >
              <Trash2 className="h-3.5 w-3.5" />
              Delete
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
            <Button size="sm" className="gap-1.5" onClick={handleDeploy} disabled={isDeploying}>
              <Rocket className="h-3.5 w-3.5" />
              {isDeploying ? "Deploying…" : "Deploy"}
            </Button>
          </div>
        </div>

        <Tabs
          value={queryParams.tab}
          onValueChange={(tab) => setQueryParams({ tab })}
          className="flex flex-col gap-4"
        >
          <TabsList className="w-fit flex-wrap">
            {TABS.map((tab) => {
              const Icon = tab.icon;
              return (
                <TabsTrigger key={tab.value} value={tab.value} className="gap-1.5">
                  <Icon className="h-4 w-4" />
                  {tab.label}
                </TabsTrigger>
              );
            })}
          </TabsList>

          <TabsContent value="code" className="grid grid-cols-1 gap-6 lg:grid-cols-3">
            <div className="space-y-4 lg:col-span-2">
              <div className="flex items-center gap-1 border-b">
                {(["index.js", "package.json"] as const).map((file) => (
                  <button
                    key={file}
                    className={
                      "border-b-2 px-3 py-2 font-mono text-sm " +
                      (activeFile === file
                        ? "border-primary text-foreground"
                        : "border-transparent text-muted-foreground hover:text-foreground")
                    }
                    onClick={() => setActiveFile(file)}
                  >
                    {file}
                  </button>
                ))}
              </div>
              {activeFile === "index.js" ? (
                <CodeEditor language="javascript" value={indexJs} onChange={setIndexJs} height="480px" />
              ) : (
                <CodeEditor language="json" value={packageJson} onChange={setPackageJson} height="480px" />
              )}
              <SandboxHelpCard />
            </div>
            <div className="space-y-4">
              <Card>
                <CardContent className="pt-6">
                  <TestPanel functionId={functionId} />
                </CardContent>
              </Card>
            </div>
          </TabsContent>

          <TabsContent value="configuration" className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            <Card>
              <CardContent className="space-y-1.5 pt-6">
                <h3 className="mb-3 text-sm font-semibold">Limits</h3>
                <LimitsForm value={limits} onChange={setLimits} />
              </CardContent>
            </Card>
            <Card>
              <CardContent className="space-y-1.5 pt-6">
                <h3 className="mb-3 text-sm font-semibold">Retry policy</h3>
                <RetryForm value={retry} onChange={setRetry} />
              </CardContent>
            </Card>
            <Card className="lg:col-span-2">
              <CardContent className="pt-6">
                <VariablesEditor value={variables} onChange={setVariables} />
              </CardContent>
            </Card>
          </TabsContent>

          <TabsContent value="trigger" className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            <div className="space-y-6">
              <TriggerHttpCard value={trigger} onChange={setTrigger} functionId={fn.id} />
              <TriggerWorkflowCard value={trigger} onChange={setTrigger} />
            </div>
            {trigger.httpEnabled && (
              <InvokeSnippetCard functionId={fn.id} trigger={trigger} />
            )}
          </TabsContent>

          <TabsContent value="output">
            <OutputActionsEditor value={outputActions} onChange={setOutputActions} />
          </TabsContent>

          <TabsContent value="runs" className="space-y-4">
            {queryParams.runId ? (
              <div className="space-y-3">
                <Button variant="ghost" size="sm" onClick={() => setQueryParams({ runId: "" })}>
                  ← Back to runs
                </Button>
                <RunDetail runId={queryParams.runId} />
              </div>
            ) : (
              <RunsTable
                runs={runsData?.data ?? []}
                isLoading={isRunsLoading}
                functionRoutePrefix={`functions/${functionId}`}
              />
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
          totalRuns: 0,
          lastUpdatedDate: "",
        }}
        onDeleted={() => navigate(scoped("functions"))}
      />
    </div>
  );
};

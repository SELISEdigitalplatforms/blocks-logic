"use client";

import { useState, useEffect, useRef } from "react";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { Button } from "@/components/ui-kits/button/button";
import {
  Save,
  Loader2,
  AlertCircle,
  X,
  EllipsisVertical,
  Pen,
  Copy,
  Download,
  Trash,
  Settings2,
} from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { WorkflowEditor } from "../../components/workflow-editor";
import { ReactFlowProvider } from "@xyflow/react";
import { WorkflowStoreProvider, useWorkflowStoreApi } from "../../store";
import { useWorkflow, useAutoSaveWorkflow } from "../../hooks";
import { Separator } from "@/components/ui-kits/separator/separator";
import { format } from "date-fns";
import { useGetWorkflowById, useUpdateWorkflow } from "@blocks-workflow/hooks/use-workflow-api";
import {
  hasLegacyClientCredential,
  upgradeClientCredentialParameters,
} from "../../utils/client-credential-upgrade";
import { toPersistedNode } from "../../utils/to-persisted-node";
import { WorkflowExecutions } from "@blocks-workflow/components/workflow-execution";
import { useLocation, useNavigate, useParams } from "react-router";
import { showErrorToast } from "@/hooks/use-toast";
import { PublishWorkflowAction } from "../../components/publish-workflow-action";
import { WorkflowVersions } from "../../components/workflow-version";
import { useGetLastSuccessfulExecution } from "../../hooks";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { DeleteWorkflow } from "../../components/delete-workflow";
import { DuplicateWorkflow } from "../../components/duplicate-workflow";
import { RenameWorkflow } from "../../components/rename-workflow/rename-workflow";
import { WorkflowSettings } from "../../components/workflow-settings/workflow-settings";
import { useExportWorkflow } from "../../hooks/use-export-workflow";

type WorkflowDetailPageProps = {
  workflowId: string;
};

export const WorkflowDetailsContent = ({ workflowId }: WorkflowDetailPageProps) => {
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const scoped = useScopedPath();
  const { exportWorkflow, isExporting } = useExportWorkflow();

  const [activeTab, setActiveTab] = useState<string>("editor");
  const [isDirtyBannerDismissed, setIsDirtyBannerDismissed] = useState(false);
  const [modal, setModal] = useState<"rename" | "duplicate" | "settings" | "delete" | null>(null);

  const { hasUnsavedChanges, setWorkflow, setLastSuccessfulExecutionData } = useWorkflow();
  const workflowStore = useWorkflowStoreApi();
  const { mutateAsync: updateWorkflow } = useUpdateWorkflow();
  // Workflows whose legacy client credential fields were already saved away, so a refetch never repeats it.
  const migratedWorkflowIds = useRef(new Set<string>());

  const { data, isLoading, isFetched, isFetchedAfterMount, refetch } =
    useGetWorkflowById({ id: workflowId });
  const { data: lastExecutionData, isFetched: isLastExecutionFetched } =
    useGetLastSuccessfulExecution({ workflowId });

  useEffect(() => {
    if (isLastExecutionFetched && lastExecutionData) {
      setLastSuccessfulExecutionData(lastExecutionData || null);
    }
  }, [lastExecutionData, isLastExecutionFetched, setLastSuccessfulExecutionData]);

  useEffect(() => {
    if (isFetched && isFetchedAfterMount) {
      if (data?.data) {
        const workflowData = data.data;
        // A reload (after a save, or another change) must not overwrite edits not saved yet: they are newer
        // than the server's copy. Read from the store, not render state, so this runs only for new data.
        const editor = workflowStore.getState();
        if (editor.hasUnsavedChanges && editor.workflowId === workflowData.itemId) return;
        setWorkflow(workflowData);

        // Save once so the legacy id:::secret composite and stored secret leave the workflow. Only that
        // change is saved: the stored nodes, not the store's copy (its other upgrades wait for a real edit).
        const needsMigration = workflowData.nodes?.some(hasLegacyClientCredential);
        if (needsMigration && !migratedWorkflowIds.current.has(workflowData.itemId)) {
          migratedWorkflowIds.current.add(workflowData.itemId);
          updateWorkflow({
            itemId: workflowData.itemId,
            nodes: workflowData.nodes.map((node) => toPersistedNode(upgradeClientCredentialParameters(node))),
            edges: workflowData.edges,
          }).catch(() => migratedWorkflowIds.current.delete(workflowData.itemId));
        }
      } else {
        showErrorToast({ errors: "Workflow not found" });
        navigate(scoped("/app/workflow"));
      }
    }
    // `scoped` is deliberately not a dependency (unchanged from before): a new scoped-path function
    // would re-run this effect and re-apply the fetched workflow to the store.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data, isFetched, isFetchedAfterMount, setWorkflow, navigate, workflowStore, updateWorkflow]);

  const { isSaving, saveNow } = useAutoSaveWorkflow({
    workflowId,
    debounceMs: 20000,
    enabled: false,
    onSaveSuccess: () => {},
    onSaveError: (_error) => {},
  });

  const handleManualSave = () => saveNow();
  const showDirtyBanner =
    !isLoading && isFetchedAfterMount && data?.data?.isDirty && !isDirtyBannerDismissed;

  // Re-show the dirty banner when the workflow or its dirty flag changes; adjusted during render
  // (React's "adjust state when a prop changes" pattern).
  const isDirty = data?.data?.isDirty;
  const [bannerResetFrom, setBannerResetFrom] = useState({ workflowId, isDirty });
  if (bannerResetFrom.workflowId !== workflowId || bannerResetFrom.isDirty !== isDirty) {
    setBannerResetFrom({ workflowId, isDirty });
    setIsDirtyBannerDismissed(false);
  }

  const workflowName = data?.data?.name || "";
  return (
    <>
      <div className="flex h-full flex-col">
        <div className="px-4 mt-4">
          <PageBreadcrumb breadcrumbIndex={3} customTitles={{ [pathname]: workflowName || null }} />
        </div>

        {showDirtyBanner && (
          <div className="rounded-lg mx-4 mt-4 bg-yellow-50 border border-yellow-500 text-yellow-700 dark:bg-yellow-950/60 dark:text-yellow-300 dark:border dark:border-yellow-700 p-2.5">
            <div className="flex items-center justify-between gap-3">
              <div className="flex items-center gap-3">
                <AlertCircle className="h-5 w-5 text-amber-500" />
                <p className="text-sm font-medium">
                  You have unadapted changes. Please click on the Publish button to adapt them.
                </p>
              </div>
              <button
                type="button"
                aria-label="Dismiss unadapted changes warning"
                onClick={() => setIsDirtyBannerDismissed(true)}
                className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-yellow-700 transition-colors hover:bg-yellow-100 focus:outline-none focus:ring-2 focus:ring-yellow-500 dark:text-yellow-300 dark:hover:bg-yellow-900/70"
              >
                <X className="h-4 w-4" />
              </button>
            </div>
          </div>
        )}
        {isLoading || !isFetchedAfterMount ? (
          <div className="flex h-full items-center justify-center">
            <Loader2 className="h-8 w-8 animate-spin text-primary" />
          </div>
        ) : (
          <Tabs
            value={activeTab}
            className="flex w-full flex-1 flex-col overflow-hidden"
            onValueChange={(v) => {
              setActiveTab(v);
            }}
          >
            <div className="flex items-center justify-between border-b px-4 py-4">
              <TabsList>
                <TabsTrigger value="editor">Editor</TabsTrigger>
                <TabsTrigger value="executions">Executions</TabsTrigger>
                <TabsTrigger value="versions">Versions</TabsTrigger>
              </TabsList>

              {activeTab === "editor" && (
                <div className="flex items-center gap-4">
                  {/* <div className={`text-sm font-medium ${data?.data?.isPublished ? "text-green-500" : "text-yellow-500"}`}>
                  {data?.data?.isPublished ? "Published" : "Unpublished"}
                </div>
                <Separator
                  orientation="vertical"
                  className="h-4 bg-muted-foreground"
                /> */}
                  <div className="text-sm text-muted-foreground">
                    {isSaving ? (
                      <span className="flex items-center gap-1">
                        <Loader2 className="h-3 w-3 animate-spin" />
                        Saving...
                      </span>
                    ) : data?.data?.lastUpdatedDate ? (
                      `Last saved: ${format(new Date(data.data.lastUpdatedDate), "dd/MM/yyyy, hh:mm a")}`
                    ) : (
                      "Not saved yet"
                    )}
                  </div>
                  {hasUnsavedChanges && !isSaving && (
                    <span className="text-xs text-orange-500">Unsaved changes</span>
                  )}
                  <Separator orientation="vertical" className="h-4 bg-muted-foreground" />
                  <div className="flex items-center gap-2">
                    <PublishWorkflowAction
                      isDirty={data?.data?.isDirty}
                      hasUnsavedChanges={hasUnsavedChanges}
                      isPublished={data?.data?.isPublished}
                      onActionComplete={() => refetch()}
                    />
                  </div>
                  <Separator orientation="vertical" className="h-4 bg-muted-foreground" />
                  {/* <Button variant="outline" size="sm" className="gap-2">
                  <ScrollText className="h-4 w-4" />
                  Logs
                </Button> */}
                  <Button
                    size="sm"
                    onClick={handleManualSave}
                    disabled={isSaving || !hasUnsavedChanges}
                    className="gap-2"
                  >
                    {isSaving ? (
                      <Loader2 className="h-4 w-4 animate-spin" />
                    ) : (
                      <Save className="h-4 w-4" />
                    )}
                    Save
                  </Button>
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button variant="ghost" size="sm" className="h-8 w-8 p-0">
                        <EllipsisVertical className="h-4 w-4" />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem
                        className="cursor-pointer"
                        onClick={() => setModal("rename")}
                      >
                        <Pen className="mr-2 h-4 w-4" />
                        <span>Rename</span>
                      </DropdownMenuItem>
                      <DropdownMenuItem
                        className="cursor-pointer"
                        onClick={() => setModal("duplicate")}
                      >
                        <Copy className="mr-2 h-4 w-4" />
                        <span>Duplicate</span>
                      </DropdownMenuItem>
                      <DropdownMenuItem
                        className="cursor-pointer"
                        onClick={() => setModal("settings")}
                      >
                        <Settings2 className="mr-2 h-4 w-4" />
                        <span>Settings</span>
                      </DropdownMenuItem>
                      <DropdownMenuItem
                        className="cursor-pointer"
                        disabled={isExporting}
                        onClick={() => void exportWorkflow(workflowId)}
                      >
                        <Download className="mr-2 h-4 w-4" />
                        <span>Export</span>
                      </DropdownMenuItem>
                      <DropdownMenuItem
                        className="cursor-pointer text-error"
                        onClick={() => setModal("delete")}
                      >
                        <Trash className="mr-2 h-4 w-4" />
                        <span>Delete</span>
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              )}
            </div>

            <TabsContent value="editor" className="flex-1 overflow-hidden">
              <div className="flex h-full w-full">
                <div className="relative h-full flex-1">
                  <WorkflowEditor />
                </div>
              </div>
            </TabsContent>

            <TabsContent value="executions" className="flex-1 overflow-hidden">
              <WorkflowExecutions />
            </TabsContent>

            <TabsContent value="versions" className="flex-1 overflow-hidden">
              <WorkflowVersions sidebarPosition="left" />
            </TabsContent>
          </Tabs>
        )}
      </div>
      <RenameWorkflow
        key={modal === "rename" ? workflowName : "closed"}
        open={modal === "rename"}
        onOpenChange={(value) => {
          if (!value) setModal(null);
        }}
        workflowId={workflowId}
        initialName={workflowName}
      />
      <DuplicateWorkflow
        open={modal === "duplicate"}
        onOpenChange={(value) => {
          if (!value) setModal(null);
        }}
        workflowId={workflowId}
        name={workflowName}
      />
      <WorkflowSettings
        // Re-created on each open, so closing without saving discards the changes.
        key={modal === "settings" ? "settings-open" : "settings-closed"}
        open={modal === "settings"}
        onOpenChange={(value) => {
          if (!value) setModal(null);
        }}
        workflowId={workflowId}
        settings={data?.data?.settings}
      />
      <DeleteWorkflow
        open={modal === "delete"}
        onOpenChange={(value) => {
          if (!value) setModal(null);
        }}
        workflowId={workflowId}
        onDeleted={() => navigate(scoped("workflow"))}
      />
    </>
  );
};

export const WorkflowDetails = () => {
  const { id } = useParams<{ id: string }>();
  if (!id) return;
  return (
    <ReactFlowProvider key={id}>
      <WorkflowStoreProvider>
        <WorkflowDetailsContent workflowId={id} />
      </WorkflowStoreProvider>
    </ReactFlowProvider>
  );
};

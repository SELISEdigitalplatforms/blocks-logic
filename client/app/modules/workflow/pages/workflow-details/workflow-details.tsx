"use client";

import { useState, useEffect } from "react";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui-kits/tabs/tabs";
import { Button } from "@/components/ui-kits/button/button";
import { ScrollText, Save, Loader2, AlertCircle } from "lucide-react";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { WorkflowEditor } from "../../components/workflow-editor";
import { ReactFlowProvider } from "@xyflow/react";
import { WorkflowStoreProvider } from "../../store";
import { useWorkflow, useAutoSaveWorkflow } from "../../hooks";
import { Separator } from "@/components/ui-kits/separator/separator";
import { format } from "date-fns";
import { useGetWorkflowById } from "@blocks-workflow/hooks/use-workflow-api";
import { WorkflowExecutions } from "@blocks-workflow/components/workflow-execution";
import { useNavigate, useParams } from "react-router-dom";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { showErrorToast } from "@/hooks/use-toast";
import { PublishWorkflowAction } from "../../components/publish-workflow-action";
import { WorkflowVersions } from "../../components/workflow-version";

type WorkflowDetailPageProps = {
  workflowId: string;
};

export const WorkflowDetailsContent = ({
  workflowId,
}: WorkflowDetailPageProps) => {
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  const navigate = useNavigate();
  const [activeTab, setActiveTab] = useState<string>("editor");
  const { hasUnsavedChanges, setWorkflow } = useWorkflow();
  const { data, isLoading, isFetched, isFetching, isFetchedAfterMount, refetch } =
    useGetWorkflowById({
      id: workflowId,
      projectKey,
    });

  useEffect(() => {
    if (isFetched && isFetchedAfterMount) {
      if (data?.data) {
        const workflowData = data.data;
        setWorkflow(workflowData);
      } else {
        showErrorToast({"errors": "Workflow not found"});
        navigate("/app/workflow");
      }
    }
  }, [data, isFetched, isFetchedAfterMount, setWorkflow, navigate]);

  const { isSaving, saveNow } = useAutoSaveWorkflow({
    workflowId,
    projectKey,
    debounceMs: 20000,
    enabled: false,
    onSaveSuccess: () => {},
    onSaveError: (_error) => {},
  });

  const handleManualSave = () => saveNow();

  BREADCRUMB_CUSTOM_TITLES[`/workflow/${workflowId}`] =
    data?.data?.name || "Workflow Details";
  return (
    <>
      <div className="flex h-full flex-col">
        <div className="px-4 mt-4">
          <PageBreadcrumb />
        </div>
        {!isLoading && isFetchedAfterMount && data?.data?.isDirty && (
          <div className="rounded-lg mx-4 mt-4 bg-yellow-50 border border-yellow-500 text-yellow-700 dark:bg-yellow-950/60 dark:text-yellow-300 dark:border dark:border-yellow-700 p-2.5">
            <div className="flex items-center gap-3">
              <AlertCircle className="h-5 w-5 text-amber-500" />
              <p className="text-sm font-medium">You have unadapted changes. Please click on the Publish button to adapt them.</p>
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

              {activeTab==="editor" && (<div className="flex items-center gap-4">
                <div className={`text-sm font-medium ${data?.data?.isPublished ? "text-green-500" : "text-yellow-500"}`}>
                  {data?.data?.isPublished ? "Published" : "Unpublished"}
                </div>
                <Separator
                  orientation="vertical"
                  className="h-4 bg-muted-foreground"
                />
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
                  <span className="text-xs text-orange-500">
                    Unsaved changes
                  </span>
                )}
                <Separator
                  orientation="vertical"
                  className="h-4 bg-muted-foreground"
                />
                <div className="flex items-center gap-2">
                  <PublishWorkflowAction 
                    isDirty={data?.data?.isDirty} 
                    hasUnsavedChanges={hasUnsavedChanges}
                    isPublished={data?.data?.isPublished} 
                    onActionComplete={() => refetch()} 
                  />
                </div>
                <Separator
                  orientation="vertical"
                  className="h-4 bg-muted-foreground"
                />
                <Button variant="outline" size="sm" className="gap-2">
                  <ScrollText className="h-4 w-4" />
                  Logs
                </Button>
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
              </div>)}
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
    </>
  );
};

export const WorkflowDetails = () => {
  const { id } = useParams<{ id: string }>();
  if (!id) return;
  return (
    <ReactFlowProvider>
      <WorkflowStoreProvider>
        <WorkflowDetailsContent workflowId={id} />
      </WorkflowStoreProvider>
    </ReactFlowProvider>
  );
};

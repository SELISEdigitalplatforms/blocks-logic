"use client";

import { useState, useEffect } from "react";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui-kits/tabs/tabs";
import { Button } from "@/components/ui-kits/button/button";
import { Switch } from "@/components/ui-kits/switch/switch";
import { ScrollText, Save, Loader2 } from "lucide-react";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { WorkflowEditor } from "../../components/workflow-editor";
import { ReactFlowProvider } from "@xyflow/react";
import { WorkflowStoreProvider } from "../../store";
import { useWorkflow, useAutoSaveWorkflow } from "../../hooks";
import { Separator } from "@/components/ui-kits/separator/separator";
import { ToggleStatusWorkflow } from "../../components/toggle-status-workflow";
import { format } from "date-fns";
import { useGetWorkflowById } from "@blocks-workflow/hooks/use-workflow-api";
import { WorkflowExecutions } from "@blocks-workflow/components/workflow-execution";
import { useNavigate, useParams } from "react-router-dom";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { showErrorToast } from "@/hooks/use-toast";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { History } from "lucide-react";
import { VersionHistorySidebar } from "../../components/version-history-sidebar";
import { PublishWorkflowAction } from "../../components/publish-workflow-action";

type WorkflowDetailPageProps = {
  workflowId: string;
};

export const WorkflowDetailsContent = ({
  workflowId,
}: WorkflowDetailPageProps) => {
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  const navigate = useNavigate();
  const [isToggleStatusModalOpen, setIsToggleStatusModalOpen] = useState(false);
  const [isVersionHistoryMode, setIsVersionHistoryMode] = useState(false);
  const { isDirty, isActive: workflowIsActive, setWorkflow } = useWorkflow();
  const { data, isLoading, isFetched, isFetching, isFetchedAfterMount } =
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
        navigate("/workflow");
      }
    }
  }, [data, isFetched, isFetchedAfterMount, setWorkflow, navigate]);

  const { isSaving, saveNow } = useAutoSaveWorkflow({
    workflowId,
    projectKey,
    debounceMs: 20000,
    enabled: true,
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
        {isLoading || !isFetchedAfterMount ? (
          <div className="flex h-full items-center justify-center">
            <Loader2 className="h-8 w-8 animate-spin text-primary" />
          </div>
        ) : (
          <Tabs
            defaultValue="editor"
            className="flex w-full flex-1 flex-col overflow-hidden"
            onValueChange={(v) => {
              if (v === "editor" && data?.data) {
                const workflowData = data.data;
                setWorkflow(workflowData);
              }
            }}
          >
            <div className="flex items-center justify-between border-b px-4 py-4">
              <TabsList>
                <TabsTrigger value="editor">Editor</TabsTrigger>
                <TabsTrigger value="executions">Executions</TabsTrigger>
              </TabsList>

              <div className="flex items-center gap-4">
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
                {isDirty && !isSaving && (
                  <span className="text-xs text-orange-500">
                    Unsaved changes
                  </span>
                )}
                <Separator
                  orientation="vertical"
                  className="h-4 bg-muted-foreground"
                />
                <div className="flex items-center gap-2">
                  <PublishWorkflowAction />
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-9 w-9"
                    onClick={() => setIsVersionHistoryMode(true)}
                  >
                    <History className="h-4 w-4" />
                  </Button>
                </div>
                <Separator
                  orientation="vertical"
                  className="h-4 bg-muted-foreground"
                />
                <div className="flex items-center gap-2">
                  <span className="text-sm text-medium-emphasis">
                    {workflowIsActive ? "Active" : "Inactive"}
                  </span>
                  <Switch
                    size="sm"
                    checked={workflowIsActive}
                    onCheckedChange={(_checked) =>
                      setIsToggleStatusModalOpen(true)
                    }
                  />
                </div>
                <Button variant="outline" size="sm" className="gap-2">
                  <ScrollText className="h-4 w-4" />
                  Logs
                </Button>
                <Button
                  size="sm"
                  onClick={handleManualSave}
                  disabled={isSaving || !isDirty}
                  className="gap-2"
                >
                  {isSaving ? (
                    <Loader2 className="h-4 w-4 animate-spin" />
                  ) : (
                    <Save className="h-4 w-4" />
                  )}
                  Save
                </Button>
              </div>
            </div>

            <TabsContent value="editor" className="flex-1 overflow-hidden">
              <div className="flex h-full w-full">
                <div className="flex-1 relative h-full">
                  <WorkflowEditor />
                  {isVersionHistoryMode && (
                    <div className="absolute inset-0 z-10 bg-black/5" />
                  )}
                </div>
                {isVersionHistoryMode && (
                  <VersionHistorySidebar onClose={() => setIsVersionHistoryMode(false)} />
                )}
              </div>
            </TabsContent>

            <TabsContent value="executions" className="flex-1 overflow-hidden">
              <WorkflowExecutions />
            </TabsContent>
          </Tabs>
        )}
      </div>
      <ToggleStatusWorkflow
        open={isToggleStatusModalOpen}
        onOpenChange={setIsToggleStatusModalOpen}
        isActive={workflowIsActive}
        workflowId={workflowId}
      />
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

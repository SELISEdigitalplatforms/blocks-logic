import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Button } from "@/components/ui-kits/button/button";
import { MoreVertical, X, Loader2 } from "lucide-react";
import { useParams } from "react-router-dom";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useGetWorkflowVersions, usePublishWorkflow, useRestoreWorkflow } from "../../hooks/use-workflow-api";
import { format } from "date-fns";

interface VersionHistorySidebarProps {
  onClose: () => void;
  onSelectVersion: (version: any) => void;
}

export const VersionHistorySidebar = ({ onClose, onSelectVersion }: VersionHistorySidebarProps) => {
  const { id: workflowId } = useParams<{ id: string }>();
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  
  const { data: versionsData, isLoading } = useGetWorkflowVersions({
    projectKey,
    workflowId: workflowId || "",
  });

  const publishWorkflow = usePublishWorkflow();
  const restoreWorkflow = useRestoreWorkflow();

  const handlePublish = (version: any) => {
    publishWorkflow.mutate({
      workflowId: workflowId || "",
      projectKey,
      name: version.name || "Published Version",
    });
  };

  const handleRestore = (version: any) => {
    restoreWorkflow.mutate({
      workflowId: workflowId || "",
      projectKey,
      versionId: version.itemId || version.id,
    });
  };

  const rawVersions = versionsData?.data || [];
  const versions = Array.isArray(rawVersions) ? rawVersions : [];
  return (
    <div className="w-80 h-full border-l border-border bg-background flex flex-col">
      <div className="flex items-center justify-between p-4 border-b border-border">
        <h3 className="font-semibold text-lg">Version History</h3>
        <Button variant="ghost" size="icon" onClick={onClose} className="h-8 w-8">
          <X className="h-4 w-4" />
        </Button>
      </div>
      <div className="flex-1 overflow-y-auto p-4 space-y-4">
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
          </div>
        ) : versions.length === 0 ? (
          <div className="text-center text-sm text-muted-foreground py-8">
            No versions found.
          </div>
        ) : (
          versions.map((version: any) => (
            <div 
              key={version.itemId || version.id || Math.random()} 
              className="flex flex-col gap-1 p-2 rounded-md hover:bg-muted/50 relative group cursor-pointer"
              onClick={() => onSelectVersion(version)}
            >
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2">
                  {version.isActive ? (
                    <div className="w-2 h-2 rounded-full bg-yellow-500" />
                  ) : (
                    <div className="w-2 h-2 rounded-full border-2 border-muted-foreground" />
                  )}
                  <span className="font-medium text-sm">{version.name || "Unnamed Version"}</span>
                </div>
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button variant="ghost" size="icon" className="h-6 w-6 opacity-0 group-hover:opacity-100 transition-opacity">
                      <MoreVertical className="h-4 w-4" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem onClick={() => handleRestore(version)}>Restore version</DropdownMenuItem>
                    <DropdownMenuItem onClick={() => handlePublish(version)}>Publish version</DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              </div>
              <span className="text-xs text-muted-foreground pl-4">
                {version.author || version.createdBy || "Unknown author"},{" "}
                {version.date || version.createdDate ? format(new Date(version.date || version.createdDate), "MMM dd 'at' HH:mm:ss") : "Unknown date"}
              </span>
            </div>
          ))
        )}
      </div>
    </div>
  );
};

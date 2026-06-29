import { Button } from "@/components/ui-kits/button/button";
import { MoreVertical, Loader2, Info } from "lucide-react";
import { useParams } from "react-router-dom";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useGetWorkflowVersions } from "../../hooks/use-workflow-api";
import { formatDate } from "@/lib/utils";
import { WorkflowVersion } from "../../models/workflow.model";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import { WorkflowVersionActionDropdown } from "../workflow-version/workflow-version-action-dropdown";

interface VersionHistorySidebarProps {
  onClose?: () => void;
  onSelectVersion: (version: any) => void;
  selectedVersionId?: string | null;
}

export const VersionHistorySidebar = ({ onClose, onSelectVersion, selectedVersionId }: VersionHistorySidebarProps) => {
  const { id: workflowId } = useParams<{ id: string }>();
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  
  const { data: versionsData, isLoading } = useGetWorkflowVersions({
    projectKey,
    workflowId: workflowId || "",
  });

  const rawVersions = versionsData?.data || [];
  const unsortedVersions = Array.isArray(rawVersions) ? rawVersions : [];
  const versions = unsortedVersions.sort((a, b) => new Date(b.lastUpdatedDate).getTime() - new Date(a.lastUpdatedDate).getTime());
  return (
    <TooltipProvider>
    <div className="w-80 h-full border-l border-border bg-background flex flex-col">
      {/* <div className="flex items-center justify-between p-4 border-b border-border">
        <h3 className="font-semibold text-lg">Version History</h3>
        {onClose && (
          <Button variant="ghost" size="icon" onClick={onClose} className="h-8 w-8">
            <X className="h-4 w-4" />
          </Button>
        )}
      </div> */}
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
          versions.map((version: WorkflowVersion) => {
            const isSelected = selectedVersionId === version.itemId;
            return (
            <div 
              key={version.itemId} 
              className={`flex flex-col gap-1 p-2 rounded-md hover:bg-muted/50 relative group cursor-pointer ${
                isSelected ? "bg-muted" : ""
              }`}
              onClick={() => onSelectVersion(version)}
            >
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2 overflow-hidden">
                  {version.isPublished ? (
                    <div className="w-2 h-2 rounded-full bg-yellow-500 flex-shrink-0" />
                  ) : (
                    <div className="w-2 h-2 rounded-full border-2 border-muted-foreground flex-shrink-0" />
                  )}
                  <span className="font-medium text-sm truncate">{version.name || "Unnamed Version"}</span>
                  <span className="font-medium text-sm">{version.isPublished && " (Published)"}</span>

                </div>
                <WorkflowVersionActionDropdown version={version}>
                  <Button variant="ghost" size="icon" className="h-6 w-6 opacity-0 group-hover:opacity-100 transition-opacity flex-shrink-0">
                    <MoreVertical className="h-4 w-4" />
                  </Button>
                </WorkflowVersionActionDropdown>
              </div>
              {version.description && (
                <div className="flex items-center gap-1 pl-4 mr-2">
                  <p className="text-xs text-muted-foreground truncate flex-1">
                    {version.description}
                  </p>
                  <Tooltip delayDuration={300}>
                    <TooltipTrigger asChild>
                      <div 
                        className="flex items-center justify-center cursor-help"
                        onClick={(e) => e.stopPropagation()}
                      >
                        <Info className="h-3.5 w-3.5 text-muted-foreground hover:text-foreground transition-colors" />
                      </div>
                    </TooltipTrigger>
                    <TooltipContent side="bottom" className="max-w-[250px] whitespace-normal z-[100]">
                      <p className="text-sm">{version.description}</p>
                    </TooltipContent>
                  </Tooltip>
                </div>
              )}
              <span className="text-xs text-muted-foreground pl-4">
                {version.lastUpdatedDate ? formatDate(new Date(version.lastUpdatedDate)) : "Unknown date"}
              </span>
            </div>
            );
          })
        )}
      </div>
    </div>
    </TooltipProvider>
  );
};

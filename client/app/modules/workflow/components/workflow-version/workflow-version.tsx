"use client";

import { useState } from "react";
import { ReactFlowProvider } from "@xyflow/react";
import { WorkflowStoreProvider } from "../../store";
import { VersionHistorySidebar } from "../version-history-sidebar/version-history-sidebar";
import { WorkflowVersionEditor } from "./workflow-version-editor";
import { WorkflowVersion } from "../../models/workflow.model";

interface WorkflowVersionsProps {
  onClose?: () => void;
  sidebarPosition?: "left" | "right";
}

export const WorkflowVersions = ({ onClose, sidebarPosition = "right" }: WorkflowVersionsProps) => {
  const [selectedVersion, setSelectedVersion] = useState<WorkflowVersion | null>(null);

  return (
    <ReactFlowProvider>
      <WorkflowStoreProvider>
        <div className="flex h-full w-full">
          {sidebarPosition === "left" && (
            <VersionHistorySidebar 
              onClose={onClose}
              onSelectVersion={(version) => setSelectedVersion(version)}
              selectedVersionId={selectedVersion?.itemId}
            />
          )}
          <div className={`relative h-full flex-1 ${sidebarPosition === "right" ? "border-r" : "border-l"}`}>
            <WorkflowVersionEditor version={selectedVersion} />
          </div>
          {sidebarPosition === "right" && (
            <VersionHistorySidebar 
              onClose={onClose}
              onSelectVersion={(version) => setSelectedVersion(version)}
              selectedVersionId={selectedVersion?.itemId}
            />
          )}
        </div>
      </WorkflowStoreProvider>
    </ReactFlowProvider>
  );
};

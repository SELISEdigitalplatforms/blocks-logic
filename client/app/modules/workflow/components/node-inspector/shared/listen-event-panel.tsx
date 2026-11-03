"use client";

import { Button } from "@/components/ui-kits/button/button";
import { AudioLines, Square } from "lucide-react";
import { useWorkflow } from "@blocks-workflow/hooks";
import { useExecuteTriggerListener } from "@blocks-workflow/hooks/use-workflow-api";
import { useT } from "@seliseblocks/genesis-os";

export const ListenEventPanel = () => {
  const { t } = useT("logic.workflow.nodes.inspector");
  const { selectedNode, isListening, listeningNodeId, setIsListening } = useWorkflow();
  const { mutate: executeTriggerListener, isPending } = useExecuteTriggerListener();

  if (!selectedNode) return null;

  const isThisNodeListening = isListening && listeningNodeId === selectedNode.id;

  const toggleListening = () => {
    if (isThisNodeListening) {
      executeTriggerListener(
        { triggerId: selectedNode.id, enableListener: false },
        { onSuccess: () => setIsListening(false) }
      );
    } else {
      executeTriggerListener(
        { triggerId: selectedNode.id, enableListener: true, completionNodeId: selectedNode.id },
        { onSuccess: () => setIsListening(true, selectedNode.id) }
      );
    }
  };

  return (
    <div className="flex h-full w-full flex-col items-center justify-center gap-3 rounded bg-surface-app">
      <h3 className="text-lg font-semibold text-high-emphasis">{t("LISTEN_EVENT_PANEL.RECEIVE_DATA_FROM_A")}</h3>
      {isThisNodeListening ? (
        <div className="flex flex-col items-center gap-4 mt-2">
          <div className="flex items-center text-sm font-medium text-green-600">
            <span className="relative flex h-3 w-3 mr-2">
              <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-green-400 opacity-75"></span>
              <span className="relative inline-flex h-3 w-3 rounded-full bg-green-500"></span>
            </span>
            <span>{t("LISTEN_EVENT_PANEL.LISTENING_FOR_EVENTS")}</span>
          </div>
          <Button variant="outline" onClick={toggleListening} disabled={isPending}>
            <Square className="h-4 w-4" />
            <span className="ml-2">{t("LISTEN_EVENT_PANEL.STOP_LISTENING")}</span>
          </Button>
        </div>
      ) : (
        <Button variant="outline" onClick={toggleListening} disabled={isPending}>
          <AudioLines className="h-4 w-4" />
          <span className="ml-2">{t("LISTEN_EVENT_PANEL.LISTEN_FOR_TEST_EVENT")}</span>
        </Button>
      )}
      <p className="text-sm font-normal text-medium-emphasis">
        {t("LISTEN_EVENT_PANEL.TRIGGER_THIS_WORKFLOW_AUTOMATICALLY")}
      </p>
    </div>
  );
};

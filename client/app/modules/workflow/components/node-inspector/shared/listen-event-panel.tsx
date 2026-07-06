"use client";

import { Button } from "@/components/ui-kits/button/button";
import { AudioLines, Square } from "lucide-react";
import { useWorkflow } from "@blocks-workflow/hooks";
import { useExecuteTriggerListener } from "@blocks-workflow/hooks/use-workflow-api";

export const ListenEventPanel = () => {
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
        { triggerId: selectedNode.id, enableListener: true },
        { onSuccess: () => setIsListening(true, selectedNode.id) }
      );
    }
  };

  return (
    <div className="flex h-full w-full flex-col items-center justify-center gap-3 rounded bg-surface-app">
      <h3 className="text-lg font-semibold text-high-emphasis">Receive data from a webhook</h3>
      {isThisNodeListening ? (
        <div className="flex flex-col items-center gap-4 mt-2">
          <div className="flex items-center text-sm font-medium text-green-600">
            <span className="relative flex h-3 w-3 mr-2">
              <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-green-400 opacity-75"></span>
              <span className="relative inline-flex h-3 w-3 rounded-full bg-green-500"></span>
            </span>
            <span>Listening for events...</span>
          </div>
          <Button variant="outline" onClick={toggleListening} disabled={isPending}>
            <Square className="h-4 w-4" />
            <span className="ml-2">Stop Listening</span>
          </Button>
        </div>
      ) : (
        <Button variant="outline" onClick={toggleListening} disabled={isPending}>
          <AudioLines className="h-4 w-4" />
          <span className="ml-2">Listen For Test Event</span>
        </Button>
      )}
      <p className="text-sm font-normal text-medium-emphasis">
        Trigger this workflow automatically when a webhook event is received.
      </p>
    </div>
  );
};

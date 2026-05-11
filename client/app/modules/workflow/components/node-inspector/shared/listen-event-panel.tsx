"use client";

import { Button } from "@/components/ui-kits/button/button";
import { AudioLines } from "lucide-react";

export const ListenEventPanel = () => {
  return (
    <div className="flex h-full w-full flex-col items-center justify-center gap-3 rounded bg-surface-app">
      <h3 className="text-lg font-semibold text-high-emphasis">Receive data from a webhook</h3>
      <Button variant={"outline"}>
        <AudioLines className="h-4 w-4" />
        <span className="ml-2">Listen For Test Event</span>
      </Button>
      <p className="text-sm font-normal text-medium-emphasis">
        Trigger this workflow automatically when a webhook event is received.
      </p>
    </div>
  );
};

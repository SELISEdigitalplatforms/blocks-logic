"use client";

import { useRef, useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { FieldProps } from "../form-field.types";
import { SelectField } from "./select-field";
import { deriveSwitchHandle, type SwitchMode } from "@blocks-workflow/utils/switch-handles";
import { vanishedHandleKeys } from "@blocks-workflow/utils/vanished-handle-keys";

const MODE_OPTIONS = [
  { value: "rules", label: "Rules" },
  { value: "expression", label: "Expression" },
];

/** Edges of the node that a switch to `mode` would remove (they sit on outputs the other mode does not have). */
export const edgesRemovedBySwitchingMode = (
  config: FieldProps["config"],
  data: Record<string, unknown>,
  mode: SwitchMode,
): number => {
  const state = config.store.getState();
  const node = state.nodesMap[config.nodeId];
  if (!node) return 0;
  const vanished = new Set(vanishedHandleKeys(node.handle, deriveSwitchHandle({ ...data, mode })).sources);
  return Object.values(state.edgesMap).filter(
    (edge) => edge.source === config.nodeId && vanished.has(edge.sourceHandle ?? ""),
  ).length;
};

/** Mode select. Switching mode asks first when it would remove connections; Cancel keeps everything. */
export const SwitchModeField = ({ field, value, onChange, readOnly, data, config }: FieldProps<string>) => {
  const [pending, setPending] = useState<{ mode: SwitchMode; edges: number } | null>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const current: SwitchMode = value === "expression" ? "expression" : "rules";

  const request = (next: string) => {
    const mode: SwitchMode = next === "expression" ? "expression" : "rules";
    if (mode === current) return;
    const edges = edgesRemovedBySwitchingMode(config, data, mode);
    if (edges === 0) {
      onChange(mode);
      return;
    }
    // Opened after the select has closed and returned focus to itself, so Cancel keeps the focus.
    setTimeout(() => setPending({ mode, edges }), 0);
  };

  return (
    <>
      <SelectField
        field={{ ...field, type: "select", options: MODE_OPTIONS }}
        value={current}
        onChange={request}
        readOnly={readOnly}
        data={data}
        config={config}
      />
      <Dialog open={pending !== null} onOpenChange={(open) => !open && setPending(null)}>
        <DialogContent
          className="mr-4 w-full max-w-[425px] rounded-md"
          onOpenAutoFocus={(event) => {
            event.preventDefault();
            cancelRef.current?.focus();
          }}
        >
          <DialogHeader>
            <DialogTitle className="text-left text-lg font-semibold leading-7">Switch mode?</DialogTitle>
            <DialogDescription className="mt-2 break-words text-left text-sm font-normal leading-5 text-medium-emphasis">
              {pending?.edges === 1
                ? "1 connection is on an output that the other mode does not have. It will be removed."
                : `${pending?.edges} connections are on outputs that the other mode does not have. They will be removed.`}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter className="mt-4 flex flex-row gap-2">
            <Button ref={cancelRef} variant="outline" size="sm" onClick={() => setPending(null)}>
              Cancel
            </Button>
            <Button
              size="sm"
              onClick={() => {
                const mode = pending!.mode;
                setPending(null);
                onChange(mode);
              }}
            >
              Remove
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
};

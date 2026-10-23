import { useMemo, useState } from "react";
import { Loader2 } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import type { FunctionEventType, IEventCatalogItem, IFunctionEvent } from "../../types/event.types";
import {
  EVENT_NAME_HINT,
  formatPayload,
  parsePayloadJson,
  validateEventPayload,
} from "../../utils/events";
import { CodeEditor } from "../code-editor";

type EventEditDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The types the Select offers. In edit mode it holds the event's own type. */
  available: IEventCatalogItem[];
  /** The event being edited; null adds a new one. */
  initial: IFunctionEvent | null;
  /** Hides types a public function may not send, and says why. */
  isPublic: boolean;
  isSaving: boolean;
  /** Messages the server gave for this event after the last save. */
  serverErrors: string[];
  onSubmit: (event: IFunctionEvent) => void;
};

/** The first problem in the editor text, or null when it is a payload the type accepts. */
const payloadProblem = (type: string, text: string): string | null => {
  const parsed = parsePayloadJson(text);
  if (!parsed.ok) return parsed.error;
  return validateEventPayload(type as FunctionEventType, parsed.value);
};

/**
 * Add or edit one event. The type comes from the catalog only (no free text); picking one fills the
 * editor with the catalog's payload, and the person changes the values. Save stays off while the
 * text is not a valid payload.
 */
export const EventEditDialog = ({
  open,
  onOpenChange,
  available,
  initial,
  isPublic,
  isSaving,
  serverErrors,
  onSubmit,
}: Readonly<EventEditDialogProps>) => {
  const [type, setType] = useState<string>(initial?.type ?? "");
  const [text, setText] = useState<string>(initial ? formatPayload(initial.payload) : "");

  const problem = useMemo(() => (type ? payloadProblem(type, text) : null), [type, text]);
  const canSave = type !== "" && problem === null && !isSaving;
  const selected = available.find((item) => item.type === type);

  const handleTypeChange = (value: string) => {
    setType(value);
    const item = available.find((entry) => entry.type === value);
    if (item) setText(formatPayload(item.payloadTemplate));
  };

  const handleSave = () => {
    const parsed = parsePayloadJson(text);
    if (!canSave || !parsed.ok) return;
    onSubmit({ type: type as FunctionEventType, payload: parsed.value });
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl sm:rounded-xl">
        <DialogHeader>
          <DialogTitle>{initial ? "Edit event" : "Add event"}</DialogTitle>
          <DialogDescription>
            Pick an event from the list. Its payload is filled in; change the values you need.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="event-type">Event type</Label>
            <Select value={type} onValueChange={handleTypeChange} disabled={initial !== null}>
              <SelectTrigger id="event-type" aria-label="Event type">
                <SelectValue placeholder="Choose an event type" />
              </SelectTrigger>
              <SelectContent>
                {available.map((item) => (
                  <SelectItem key={item.type} value={item.type}>
                    {item.title}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {available.length === 0 && (
              <span className="text-xs text-medium-emphasis">
                Every event type that this function may send is already added.
              </span>
            )}
            {isPublic && (
              <span className="text-xs text-medium-emphasis">
                This function has a public HTTP trigger, so only events that are safe for anonymous
                callers are listed.
              </span>
            )}
            {selected && (
              <span className="text-xs text-medium-emphasis">{selected.description}</span>
            )}
          </div>

          {type !== "" && (
            <div className="flex flex-col gap-1.5">
              <Label>Payload (JSON)</Label>
              <div className="overflow-hidden rounded-md border">
                <CodeEditor language="json" value={text} onChange={setText} height="260px" />
              </div>
              {type === "FunctionEvent" && (
                <span className="text-xs text-medium-emphasis">
                  The name must match <code className="font-mono">^[a-z][a-z0-9_.-]{"{2,63}"}$</code>.{" "}
                  {EVENT_NAME_HINT}
                </span>
              )}
              {problem && (
                <span role="alert" className="text-xs text-error">
                  {problem}
                </span>
              )}
              {serverErrors.map((message) => (
                <span key={message} role="alert" className="text-xs text-error">
                  {message}
                </span>
              ))}
            </div>
          )}
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={isSaving}>
            Cancel
          </Button>
          <Button type="button" onClick={handleSave} disabled={!canSave}>
            {isSaving && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            <span>{isSaving ? "Saving…" : "Save"}</span>
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};

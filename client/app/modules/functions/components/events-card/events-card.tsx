import { useState } from "react";
import { Check, Copy, Pencil, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useGetEventCatalog, useGetEventSettings, useSaveEventSettings } from "../../hooks/use-events";
import type { IEventCatalogItem, IFunctionEvent } from "../../types/event.types";
import {
  addableCatalog,
  buildEventSnippet,
  eventTitle,
  mapSaveErrors,
  payloadName,
} from "../../utils/events";
import { EventEditDialog } from "../event-edit-dialog";
import { renderRichText } from "@/lib/rich-text";
import { useT } from "@seliseblocks/genesis-os";

type EventsCardProps = {
  functionId: string;
  /** The function's HTTP trigger is anonymous. */
  isPublic: boolean;
};

/** `null` = closed; `{ index: null }` = adding; otherwise the index of the event being edited. */
type DialogTarget = { index: number | null } | null;
const ADDING: DialogTarget = { index: null };

const CopySnippetButton = ({ event }: Readonly<{ event: IFunctionEvent }>) => {
  const { t } = useT("logic.functions.components");
  const [copied, setCopied] = useState(false);

  // The clipboard API rejects without a secure context or permission; say so instead of
  // showing "Copied" for text that never reached the clipboard.
  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(buildEventSnippet(event));
      setCopied(true);
      setTimeout(() => setCopied(false), 1400);
    } catch {
      showErrorToast({ title: t("EVENTS_CARD.COULD_NOT_COPY"), errors: [t("EVENTS_CARD.SELECT_THE_TEXT_AND")] });
    }
  };

  return (
    <Button type="button" variant="ghost" size="sm" onClick={handleCopy} aria-label={t("EVENTS_CARD.COPY_SNIPPET_FOR", { type: event.type })}>
      {copied ? <Check className="mr-1.5 h-3.5 w-3.5" /> : <Copy className="mr-1.5 h-3.5 w-3.5" />}
      <span>{copied ? t("common:COPIED") : t("EVENTS_CARD.COPY_SNIPPET")}</span>
    </Button>
  );
};

type EventRowProps = {
  event: IFunctionEvent;
  catalog: IEventCatalogItem[];
  errors: string[];
  disabled: boolean;
  onEdit: () => void;
  onRemove: () => void;
};

const EventRow = ({ event, catalog, errors, disabled, onEdit, onRemove }: Readonly<EventRowProps>) => {
  const { t } = useT("logic.functions.components");
  const name = event.type === "FunctionEvent" ? payloadName(event.payload) : null;
  return (
    <div className="flex flex-col gap-1.5 border-b px-4 py-3 last:border-b-0">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex min-w-0 flex-col gap-0.5">
          <span className="text-sm font-semibold">{eventTitle(event, catalog)}</span>
          {name && <code className="truncate font-mono text-xs text-medium-emphasis">{name}</code>}
        </div>
        <div className="flex items-center gap-1">
          <CopySnippetButton event={event} />
          <Button type="button" variant="ghost" size="sm" onClick={onEdit} disabled={disabled}>
            <Pencil className="mr-1.5 h-3.5 w-3.5" />
            <span>{t("common:EDIT")}</span>
          </Button>
          <Button type="button" variant="ghost" size="sm" onClick={onRemove} disabled={disabled}>
            <Trash2 className="mr-1.5 h-3.5 w-3.5" />
            <span>{t("common:REMOVE")}</span>
          </Button>
        </div>
      </div>
      {errors.map((message) => (
        <span key={message} role="alert" className="text-xs text-error">
          {message}
        </span>
      ))}
    </div>
  );
};

/** The Events tab: the events this function may send, each added from the catalog. */
export const EventsCard = ({ functionId, isPublic }: Readonly<EventsCardProps>) => {
  const { t } = useT("logic.functions.components");
  const { data: catalog } = useGetEventCatalog();
  const { data: settings, isLoading } = useGetEventSettings(functionId);
  const { mutateAsync: save, isPending } = useSaveEventSettings(functionId);
  const [target, setTarget] = useState<DialogTarget>(null);
  const [rowErrors, setRowErrors] = useState<Record<number, string[]>>({});

  const items = catalog?.items ?? [];
  const events = settings?.events ?? [];
  const editIndex = target?.index ?? null;
  const editing = editIndex === null ? null : (events[editIndex] ?? null);
  const available = addableCatalog(items, events, isPublic, editing?.type);

  /** Sends the new list; true when saved. Field messages go to their rows, the rest to a toast. */
  const saveEvents = async (next: IFunctionEvent[], dialogIndex: number | null): Promise<boolean> => {
    try {
      await save({ events: next });
      setRowErrors({});
      return true;
    } catch (error) {
      if (!isErrorWithErrors(error)) {
        showErrorToast({ errors: t("EVENTS_CARD.FAILED_TO_SAVE_EVENTS") });
        return false;
      }
      const mapped = mapSaveErrors(error.errors);
      setRowErrors(mapped.byEvent);
      // Rows show their own messages (the dialog shows the one it edits); the rest is a toast.
      const unplaced = Object.entries(mapped.byEvent)
        .filter(([index]) => dialogIndex !== null && Number(index) !== dialogIndex)
        .flatMap(([, messages]) => messages);
      const toasts = [...mapped.trigger, ...mapped.general, ...unplaced];
      if (toasts.length > 0 || Object.keys(mapped.byEvent).length === 0) {
        showErrorToast({ errors: toasts.length > 0 ? toasts : t("EVENTS_CARD.FAILED_TO_SAVE_EVENTS") });
      }
      return false;
    }
  };

  const handleSubmit = async (event: IFunctionEvent) => {
    const index = editIndex ?? events.length;
    const next = editIndex === null ? [...events, event] : events.map((old, i) => (i === index ? event : old));
    if (await saveEvents(next, index)) setTarget(null);
  };

  const handleRemove = async (index: number) => {
    setRowErrors({});
    await saveEvents(events.filter((_, i) => i !== index), null);
  };

  const dialogErrors = target === null ? [] : (rowErrors[editIndex ?? events.length] ?? []);

  return (
    <Card>
      <CardContent className="flex flex-col gap-4 p-5">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <span className="text-base font-semibold">{t("EVENTS_CARD.EVENTS_THIS_FUNCTION_MAY")}</span>
            <span className="text-xs leading-relaxed text-medium-emphasis">
              {renderRichText(t("EVENTS_CARD.EVENTS_ARE_SENT_ONLY"), [(children) => <code className="font-mono">{children}</code>])}
            </span>
          </div>
          <Button type="button" size="sm" onClick={() => setTarget(ADDING)} disabled={isLoading || isPending}>
            <Plus className="mr-1.5 h-4 w-4" />
            <span>{t("EVENTS_CARD.ADD_EVENT")}</span>
          </Button>
        </div>

        {isLoading && <Skeleton className="h-16 w-full" />}
        {!isLoading && events.length === 0 && (
          <span className="rounded-lg border bg-surface-app p-4 text-xs text-medium-emphasis">
            {t("EVENTS_CARD.NO_EVENTS_YET_ADD")}
          </span>
        )}
        {events.length > 0 && (
          <div className="overflow-hidden rounded-lg border">
            {events.map((event, index) => (
              <EventRow
                key={event.type}
                event={event}
                catalog={items}
                errors={target === null ? (rowErrors[index] ?? []) : []}
                disabled={isPending}
                onEdit={() => setTarget({ index })}
                onRemove={() => handleRemove(index)}
              />
            ))}
          </div>
        )}

        <span className="text-xs leading-relaxed text-medium-emphasis">
          {t("EVENTS_CARD.CHANGES_APPLY_TO_RUNS")}
        </span>
      </CardContent>

      {target !== null && (
        <EventEditDialog
          key={editIndex ?? "new"}
          open
          onOpenChange={(open) => (open ? undefined : setTarget(null))}
          available={available}
          initial={editing}
          isPublic={isPublic}
          isSaving={isPending}
          serverErrors={dialogErrors}
          onSubmit={handleSubmit}
        />
      )}
    </Card>
  );
};

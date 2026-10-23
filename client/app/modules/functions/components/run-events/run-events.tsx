import { AlertTriangle } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Card } from "@/components/ui-kits/card/card";
import { useGetRunEvents } from "../../hooks/use-events";
import type { IRunEvent, RunEventStatus } from "../../types/event.types";
import { formatAbsoluteTime, formatRelativeTime } from "../../utils/format";

const VARIANT_BY_STATUS: Record<RunEventStatus, "success" | "error" | "warning"> = {
  Sent: "success",
  Failed: "error",
  Rejected: "warning",
};

export const RunEventStatusChip = ({ status }: Readonly<{ status: RunEventStatus }>) => (
  <Badge variant={VARIANT_BY_STATUS[status] ?? "warning"} className="w-fit rounded-md px-2.5 py-0.5">
    {status}
  </Badge>
);

const RunEventRow = ({ event }: Readonly<{ event: IRunEvent }>) => (
  <div className="grid grid-cols-[minmax(0,1fr)_88px_minmax(0,1fr)_64px_96px] items-center gap-3 border-b px-4 py-3 text-xs last:border-b-0">
    <div className="flex min-w-0 flex-col gap-0.5">
      <span className="font-semibold">{event.type}</span>
      {event.name && <code className="truncate font-mono text-medium-emphasis">{event.name}</code>}
    </div>
    <RunEventStatusChip status={event.status} />
    <span className="truncate text-medium-emphasis">{event.error ?? "—"}</span>
    <span className="text-medium-emphasis">
      {event.attempts} {event.attempts === 1 ? "try" : "tries"}
    </span>
    <span className="text-medium-emphasis" title={formatAbsoluteTime(event.sentAt)}>
      {formatRelativeTime(event.sentAt)}
    </span>
  </div>
);

/** What a run sent as events. Not shown at all when it sent none and nothing was dropped. */
export const RunEvents = ({ runId }: Readonly<{ runId: string }>) => {
  const { data } = useGetRunEvents(runId);
  const items = data?.items ?? [];
  const rejected = data?.rejected ?? null;
  if (items.length === 0 && !rejected) return null;

  return (
    <Card className="overflow-hidden">
      <div className="border-b px-4 py-3 text-sm font-semibold">Events</div>
      {rejected && (
        <div
          role="alert"
          className="flex items-center gap-2 border-b bg-warning-100 px-4 py-2.5 text-xs text-warning-800"
        >
          <AlertTriangle className="h-4 w-4 shrink-0" />
          <span>Events dropped: {rejected}</span>
        </div>
      )}
      {items.map((event) => (
        <RunEventRow key={event.id} event={event} />
      ))}
    </Card>
  );
};

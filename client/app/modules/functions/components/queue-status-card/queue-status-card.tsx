import { Card, CardContent } from "@/components/ui-kits/card/card";
import { useGetQueueStatus } from "../../hooks/use-events";
import { formatAbsoluteTime, formatRelativeTime } from "../../utils/format";
import { useT } from "@seliseblocks/genesis-os";

/** How many calls wait for a sandbox slot. Reloads every 10 s while the tab is visible. */
export const QueueStatusCard = ({ functionId }: Readonly<{ functionId: string }>) => {
  const { t } = useT("logic.functions.components");
  const { data } = useGetQueueStatus(functionId);
  if (!data) return null;

  return (
    <Card>
      <CardContent className="flex flex-wrap items-center gap-x-4 gap-y-1 p-4 text-xs">
        <span className="font-semibold">
          {t("QUEUE_STATUS_CARD.WAITING_HTTP_EVENT", { httpQueued: data.httpQueued, eventQueued: data.eventQueued })}
        </span>
        {data.eventQueued > 0 && data.oldestEventQueuedAt && (
          <span className="text-medium-emphasis" title={formatAbsoluteTime(data.oldestEventQueuedAt)}>
            {t("QUEUE_STATUS_CARD.OLDEST_EVENT_WAITING_SINCE", { value: formatRelativeTime(data.oldestEventQueuedAt) })}</span>
        )}
      </CardContent>
    </Card>
  );
};

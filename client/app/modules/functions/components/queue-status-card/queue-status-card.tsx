import { Card, CardContent } from "@/components/ui-kits/card/card";
import { useGetQueueStatus } from "../../hooks/use-events";
import { formatAbsoluteTime, formatRelativeTime } from "../../utils/format";

/** How many calls wait for a sandbox slot. Reloads every 10 s while the tab is visible. */
export const QueueStatusCard = ({ functionId }: Readonly<{ functionId: string }>) => {
  const { data } = useGetQueueStatus(functionId);
  if (!data) return null;

  return (
    <Card>
      <CardContent className="flex flex-wrap items-center gap-x-4 gap-y-1 p-4 text-xs">
        <span className="font-semibold">
          Waiting: {data.httpQueued} HTTP · {data.eventQueued} event
        </span>
        {data.eventQueued > 0 && data.oldestEventQueuedAt && (
          <span className="text-medium-emphasis" title={formatAbsoluteTime(data.oldestEventQueuedAt)}>
            oldest event waiting since {formatRelativeTime(data.oldestEventQueuedAt)}
          </span>
        )}
      </CardContent>
    </Card>
  );
};

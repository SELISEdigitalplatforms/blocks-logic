import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { browserTimeZone, formatInZone } from "../../utils/timezone";

interface ZonedTimeProps {
  iso: string | null | undefined;
  tz: string;
}

/** A time in the schedule's zone, with UTC and the viewer's local time in a tooltip. "—" when null. */
export const ZonedTime = ({ iso, tz }: Readonly<ZonedTimeProps>) => {
  if (!iso) return <span className="text-muted-foreground">—</span>;

  const viewerZone = browserTimeZone();
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <time dateTime={iso} className="whitespace-nowrap">
          {formatInZone(iso, tz, "datetime")}
        </time>
      </TooltipTrigger>
      <TooltipContent>
        <p>
          UTC: {formatInZone(iso, "UTC", "datetime")} · Your time:{" "}
          {formatInZone(iso, viewerZone, "datetime")} ({viewerZone})
        </p>
      </TooltipContent>
    </Tooltip>
  );
};

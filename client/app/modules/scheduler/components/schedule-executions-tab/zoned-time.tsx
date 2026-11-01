import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { browserTimeZone, formatInZone } from "../../utils/timezone";
import { useT } from "@seliseblocks/genesis-os";

interface ZonedTimeProps {
  iso: string | null | undefined;
  tz: string;
}

/** A time in the schedule's zone, with UTC and the viewer's local time in a tooltip. "—" when null. */
export const ZonedTime = ({ iso, tz }: Readonly<ZonedTimeProps>) => {
  const { t } = useT("logic.scheduler");
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
          {t("ZONED_TIME.UTC_YOUR_TIME", {
            value: formatInZone(iso, "UTC", "datetime"),
            value2: formatInZone(iso, viewerZone, "datetime"),
            viewerZone,
          })}
        </p>
      </TooltipContent>
    </Tooltip>
  );
};

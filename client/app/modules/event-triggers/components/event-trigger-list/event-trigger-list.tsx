import { Info, Loader2 } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Switch } from "@/components/ui-kits/switch/switch";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { cn } from "@/lib/utils";
import { BlocksEventFlag, IBlocksEventCatalogItem } from "../../types/event-triggers.type";
import { useT } from "@seliseblocks/genesis-os";

const COLUMN_COUNT = 5;
const SKELETON_ROW_KEYS = ["skeleton-1", "skeleton-2", "skeleton-3", "skeleton-4", "skeleton-5"];

type EventTriggerListProps = {
  items: IBlocksEventCatalogItem[];
  isLoading: boolean;
  canManage: boolean;
  /** Keys `${eventName}:${flag}` of the switches being saved. */
  pending: ReadonlySet<string>;
  onToggle: (item: IBlocksEventCatalogItem, flag: BlocksEventFlag, next: boolean) => void;
};

export const pendingKey = (eventName: string, flag: BlocksEventFlag) => `${eventName}:${flag}`;

const HeaderCell = ({ children, className }: { children: React.ReactNode; className?: string }) => (
  <TableHead className={cn("px-6 pb-0 pt-2 text-base", className)}>
    <div className="font-bold text-medium-emphasis">{children}</div>
  </TableHead>
);

const FlagSwitch = ({
  item,
  flag,
  canManage,
  isSaving,
  onToggle,
}: {
  item: IBlocksEventCatalogItem;
  flag: BlocksEventFlag;
  canManage: boolean;
  isSaving: boolean;
  onToggle: EventTriggerListProps["onToggle"];
}) => {
  const { t } = useT("logic.event-triggers");
  const target =
    flag === "isValidForWorkflow" ? t("EVENT_TRIGGER_LIST.TARGET_WORKFLOWS") : t("EVENT_TRIGGER_LIST.TARGET_FUNCTIONS");
  const control = (
    <div className="flex items-center gap-2">
      <Switch
        checked={item[flag]}
        disabled={!canManage || isSaving}
        aria-label={t("EVENT_TRIGGER_LIST.ENABLE_FOR", { label: item.label, target })}
        onCheckedChange={(next) => onToggle(item, flag, next)}
      />
      {isSaving && <Loader2 aria-hidden="true" className="h-4 w-4 animate-spin text-muted-foreground" />}
    </div>
  );

  if (canManage) return control;
  // A disabled switch takes no pointer events, so the hover tooltip hangs on a wrapper. Keyboard and
  // screen-reader users get the same reason from the page's read-only notice.
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span className="inline-flex">
          {control}
        </span>
      </TooltipTrigger>
      <TooltipContent>{t("EVENT_TRIGGER_LIST.READ_ONLY_TOOLTIP")}</TooltipContent>
    </Tooltip>
  );
};

export const EventTriggerList = ({ items, isLoading, canManage, pending, onToggle }: EventTriggerListProps) => {
  const { t } = useT("logic.event-triggers");
  return (
    // Wide on purpose: on a tablet the table scrolls sideways instead of squeezing the columns.
    <div className="overflow-x-auto">
      <Table className="min-w-[860px] border-separate border-spacing-y-4">
        <TableHeader className="[&_tr]:border-0">
          <TableRow className="border-0">
            <HeaderCell>{t("EVENT_TRIGGER_LIST.EVENT")}</HeaderCell>
            <HeaderCell>{t("EVENT_TRIGGER_LIST.SERVICE")}</HeaderCell>
            <HeaderCell>{t("common:DESCRIPTION")}</HeaderCell>
            <HeaderCell>{t("EVENT_TRIGGER_LIST.WORKFLOW")}</HeaderCell>
            <HeaderCell>
              <span className="inline-flex items-center gap-1">
                {t("EVENT_TRIGGER_LIST.FUNCTION")}
                <Tooltip>
                  <TooltipTrigger asChild>
                    <button type="button" aria-label={t("EVENT_TRIGGER_LIST.FUNCTION_HEADER_TOOLTIP")} className="inline-flex text-muted-foreground">
                      <Info aria-hidden="true" className="h-4 w-4" />
                    </button>
                  </TooltipTrigger>
                  <TooltipContent>{t("EVENT_TRIGGER_LIST.FUNCTION_HEADER_TOOLTIP")}</TooltipContent>
                </Tooltip>
              </span>
            </HeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody className="[&_tr:last-child]:border-0">
          {isLoading &&
            SKELETON_ROW_KEYS.map((key) => (
              <TableRow key={key} className="border-0" data-testid="event-trigger-skeleton">
                <TableCell colSpan={COLUMN_COUNT} className="rounded-lg border border-border bg-background p-4">
                  <Skeleton className="h-12 w-full" />
                </TableCell>
              </TableRow>
            ))}
          {!isLoading &&
            items.map((item) => (
              <TableRow key={item.eventName} className="group border-0 transition-colors">
                <TableCell className="rounded-l-lg border-y border-l border-border bg-background px-6 py-5 text-base group-hover:bg-muted/50">
                  <div className="font-semibold">{item.label}</div>
                  <code className="mt-1 inline-block rounded-md bg-muted px-1.5 py-0.5 font-mono text-sm">
                    {item.eventName}
                  </code>
                </TableCell>
                <TableCell className="border-y border-border bg-background px-6 py-5 text-base group-hover:bg-muted/50">
                  <Badge variant="info" className="rounded-md px-2 py-0.5">
                    {item.serviceKey}
                  </Badge>
                </TableCell>
                <TableCell className="max-w-[320px] border-y border-border bg-background px-6 py-5 text-base group-hover:bg-muted/50">
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <p className="truncate text-sm text-muted-foreground">{item.description || "-"}</p>
                    </TooltipTrigger>
                    {item.description && <TooltipContent className="max-w-sm">{item.description}</TooltipContent>}
                  </Tooltip>
                </TableCell>
                <TableCell className="border-y border-border bg-background px-6 py-5 text-base group-hover:bg-muted/50">
                  <FlagSwitch
                    item={item}
                    flag="isValidForWorkflow"
                    canManage={canManage}
                    isSaving={pending.has(pendingKey(item.eventName, "isValidForWorkflow"))}
                    onToggle={onToggle}
                  />
                </TableCell>
                <TableCell className="rounded-r-lg border-y border-r border-border bg-background px-6 py-5 text-base group-hover:bg-muted/50">
                  <FlagSwitch
                    item={item}
                    flag="isValidForFunction"
                    canManage={canManage}
                    isSaving={pending.has(pendingKey(item.eventName, "isValidForFunction"))}
                    onToggle={onToggle}
                  />
                </TableCell>
              </TableRow>
            ))}
        </TableBody>
      </Table>
    </div>
  );
};

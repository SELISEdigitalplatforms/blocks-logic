import { useMemo, useState } from "react";
import { AlertCircle } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui-kits/alert/alert";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { EventTriggerList, pendingKey } from "../../components/event-trigger-list/event-trigger-list";
import { EventTriggerToolbar } from "../../components/event-trigger-toolbar/event-trigger-toolbar";
import { useCanManageEventTriggers, useGetEventTriggers } from "../../hooks/use-event-triggers-api";
import { useEventTriggerToggle } from "../../hooks/use-event-trigger-toggle";
import { BlocksEventFlag, IBlocksEventCatalogItem } from "../../types/event-triggers.type";
import {
  DEFAULT_EVENT_TRIGGER_FILTERS,
  EventTriggerFilters,
  filterEvents,
  serviceKeysOf,
} from "../../utils/filter-events";
import { useT } from "@seliseblocks/genesis-os";

const LOGIC_EVENT_TRIGGERS_MODULE = "logic.event-triggers";

const LoadError = ({ onRetry }: { onRetry: () => void }) => {
  const { t } = useT(LOGIC_EVENT_TRIGGERS_MODULE);
  return (
    <Alert variant="destructive">
      <AlertCircle className="h-4 w-4" />
      <AlertTitle>{t("EVENT_TRIGGERS.COULD_NOT_LOAD_THE")}</AlertTitle>
      <AlertDescription className="flex flex-wrap items-center gap-3">
        {t("EVENT_TRIGGERS.CHECK_YOUR_CONNECTION")}
        <Button size="sm" variant="outline" onClick={onRetry}>
          {t("common:RETRY")}
        </Button>
      </AlertDescription>
    </Alert>
  );
};

const NotSeeded = () => {
  const { t } = useT(LOGIC_EVENT_TRIGGERS_MODULE);
  return (
    <Alert>
      <AlertCircle className="h-4 w-4" />
      <AlertTitle>{t("EVENT_TRIGGERS.EVENT_CATALOG_NOT_SET")}</AlertTitle>
      <AlertDescription>{t("EVENT_TRIGGERS.NOT_SEEDED_MESSAGE")}</AlertDescription>
    </Alert>
  );
};

type CatalogBodyProps = {
  items: IBlocksEventCatalogItem[];
  visible: IBlocksEventCatalogItem[];
  isLoading: boolean;
  isFiltered: boolean;
  canManage: boolean;
  pending: ReadonlySet<string>;
  onToggle: (item: IBlocksEventCatalogItem, flag: BlocksEventFlag, next: boolean) => void;
};

/** The table, or why there are no rows. */
const CatalogBody = ({ items, visible, isLoading, isFiltered, canManage, pending, onToggle }: CatalogBodyProps) => {
  const { t } = useT(LOGIC_EVENT_TRIGGERS_MODULE);
  if (isLoading || visible.length > 0) {
    return (
      <EventTriggerList items={visible} isLoading={isLoading} canManage={canManage} pending={pending} onToggle={onToggle} />
    );
  }
  const message =
    isFiltered || items.length > 0 ? t("EVENT_TRIGGERS.NO_EVENTS_MATCH_FILTERS") : t("EVENT_TRIGGERS.CATALOG_HAS_NO_EVENTS");
  return <p className="py-12 text-center text-sm text-muted-foreground">{message}</p>;
};

export const EventTriggers = () => {
  const { t } = useT(LOGIC_EVENT_TRIGGERS_MODULE);
  const { data, isLoading, isError, refetch } = useGetEventTriggers();
  const { canManage, isLoading: isCheckingAccess } = useCanManageEventTriggers();
  const { pending, optimistic, pendingDisable, cancelDisable, handleToggle, confirmDisable } =
    useEventTriggerToggle(canManage);
  const [filters, setFilters] = useState<EventTriggerFilters>(DEFAULT_EVENT_TRIGGER_FILTERS);

  const items = useMemo(
    () =>
      (data?.items ?? []).map((item) => ({
        ...item,
        isValidForWorkflow: optimistic[pendingKey(item.eventName, "isValidForWorkflow")] ?? item.isValidForWorkflow,
        isValidForFunction: optimistic[pendingKey(item.eventName, "isValidForFunction")] ?? item.isValidForFunction,
      })),
    [data, optimistic],
  );
  const visible = useMemo(() => filterEvents(items, filters), [items, filters]);
  const serviceKeys = useMemo(() => serviceKeysOf(items), [items]);
  const isNotSeeded = !isLoading && data?.isSeeded === false;

  let content;
  if (isError) {
    content = <LoadError onRetry={() => refetch()} />;
  } else if (isNotSeeded) {
    content = <NotSeeded />;
  } else {
    content = (
      <Card>
        <CardHeader className="mb-0 flex flex-col gap-3 md:flex-row md:items-center">
          <EventTriggerToolbar filters={filters} serviceKeys={serviceKeys} onChange={setFilters} />
        </CardHeader>
        <CardContent>
          <CatalogBody
            items={items}
            visible={visible}
            isLoading={isLoading}
            isFiltered={filters !== DEFAULT_EVENT_TRIGGER_FILTERS}
            canManage={canManage}
            pending={pending}
            onToggle={handleToggle}
          />
        </CardContent>
      </Card>
    );
  }

  return (
    <section className="flex flex-col gap-6 p-4">
      <div className="flex min-h-10 flex-col justify-center gap-1">
        <h3 className="text-2xl font-bold tracking-tight">{t("EVENT_TRIGGERS.EVENT_TRIGGERS")}</h3>
        <p className="text-sm text-muted-foreground">
          {t("EVENT_TRIGGERS.CHOOSE_WHICH_PLATFORM_EVENTS")}
        </p>
        {!canManage && !isCheckingAccess && (
          // The switches are disabled for this user; say why where keyboard and screen-reader users read it too.
          <p className="text-sm text-muted-foreground">{t("EVENT_TRIGGERS.READ_ONLY_NOTICE")}</p>
        )}
      </div>

      {content}

      <Dialog open={pendingDisable !== null} onOpenChange={(open) => !open && cancelDisable()}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("EVENT_TRIGGERS.DISABLE_FOR_WORKFLOWS", { label: pendingDisable?.item.label ?? "" })}</DialogTitle>
            <DialogDescription>
              {t("EVENT_TRIGGERS.PUBLISHED_WORKFLOWS_USE", { count: pendingDisable?.count ?? 0 })}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={cancelDisable}>
              {t("common:CANCEL")}
            </Button>
            <Button variant="destructive" onClick={confirmDisable}>
              {t("EVENT_TRIGGERS.DISABLE")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  );
};

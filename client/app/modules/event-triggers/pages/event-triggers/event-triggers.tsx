import { useMemo, useState } from "react";
import { AlertCircle, Search } from "lucide-react";
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
import { Input } from "@/components/ui-kits/input/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { EventTriggerList, pendingKey } from "../../components/event-trigger-list/event-trigger-list";
import {
  useCanManageEventTriggers,
  useGetEventTriggers,
  useUpdateEventTrigger,
} from "../../hooks/use-event-triggers-api";
import { eventTriggersService } from "../../services/event-triggers.service";
import { BlocksEventFlag, IBlocksEventCatalogItem } from "../../types/event-triggers.type";
import {
  ALL_SERVICES,
  DEFAULT_EVENT_TRIGGER_FILTERS,
  EventTriggerFilters,
  filterEvents,
  serviceKeysOf,
} from "../../utils/filter-events";

export const NOT_SEEDED_MESSAGE =
  "The event catalog is not set up for this project. Ask a platform operator to run the Blocks Event Trigger seed.";

type PendingDisable = { item: IBlocksEventCatalogItem; count: number };

const flagTarget = (flag: BlocksEventFlag) => (flag === "isValidForWorkflow" ? "workflows" : "functions");

export const EventTriggers = () => {
  const { data, isLoading, isError, refetch } = useGetEventTriggers();
  const { canManage } = useCanManageEventTriggers();
  const { mutateAsync } = useUpdateEventTrigger();
  const [filters, setFilters] = useState<EventTriggerFilters>(DEFAULT_EVENT_TRIGGER_FILTERS);
  const [pending, setPending] = useState<ReadonlySet<string>>(new Set());
  // The value a switch shows while its save is in flight; dropped on failure, which reverts the switch.
  const [optimistic, setOptimistic] = useState<Record<string, boolean>>({});
  const [pendingDisable, setPendingDisable] = useState<PendingDisable | null>(null);

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

  const setFlagPending = (key: string, on: boolean) =>
    setPending((current) => {
      const next = new Set(current);
      if (on) next.add(key);
      else next.delete(key);
      return next;
    });

  const dropOptimistic = (key: string) =>
    setOptimistic((current) => {
      const { [key]: _dropped, ...rest } = current;
      return rest;
    });

  const save = async (item: IBlocksEventCatalogItem, flag: BlocksEventFlag, next: boolean) => {
    const key = pendingKey(item.eventName, flag);
    setFlagPending(key, true);
    setOptimistic((current) => ({ ...current, [key]: next }));
    try {
      const result = await mutateAsync({ eventName: item.eventName, [flag]: next });
      if (!result.isSuccess) {
        showErrorToast({ title: `Could not update ${item.label}`, errors: result.errors ?? "Something went wrong" });
        return;
      }
      showSuccessToast({ description: `${item.label} ${next ? "enabled" : "disabled"} for ${flagTarget(flag)}` });
    } catch (error) {
      showErrorToast({
        title: `Could not update ${item.label}`,
        errors: isErrorWithErrors(error) ? error.errors : "Something went wrong",
      });
    } finally {
      // On success the cached list already holds the new value; on failure this reverts the switch.
      dropOptimistic(key);
      setFlagPending(key, false);
    }
  };

  const handleToggle = async (item: IBlocksEventCatalogItem, flag: BlocksEventFlag, next: boolean) => {
    if (!canManage) return;
    if (flag === "isValidForWorkflow" && !next) {
      // Turning Workflow off stops published workflows; say how many first.
      const key = pendingKey(item.eventName, flag);
      setFlagPending(key, true);
      try {
        const usage = await eventTriggersService.getUsage(item.eventName);
        if (usage.publishedWorkflowCount > 0) {
          setPendingDisable({ item, count: usage.publishedWorkflowCount });
          return;
        }
      } catch {
        // The count is only a warning; the save itself still reports any real failure.
      } finally {
        setFlagPending(key, false);
      }
    }
    await save(item, flag, next);
  };

  const confirmDisable = async () => {
    if (!pendingDisable) return;
    const { item } = pendingDisable;
    setPendingDisable(null);
    await save(item, "isValidForWorkflow", false);
  };

  const isSeeded = data?.isSeeded !== false;
  const isFiltered = filters !== DEFAULT_EVENT_TRIGGER_FILTERS;

  return (
    <section className="flex flex-col gap-6 p-4">
      <div className="flex min-h-10 flex-col justify-center gap-1">
        <h3 className="text-2xl font-bold tracking-tight">Event Triggers</h3>
        <p className="text-sm text-muted-foreground">
          Choose which platform events can start workflows and functions in this project.
        </p>
      </div>

      {isError ? (
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertTitle>Could not load the event catalog</AlertTitle>
          <AlertDescription className="flex flex-wrap items-center gap-3">
            Check your connection and try again.
            <Button size="sm" variant="outline" onClick={() => refetch()}>
              Retry
            </Button>
          </AlertDescription>
        </Alert>
      ) : !isLoading && !isSeeded ? (
        <Alert>
          <AlertCircle className="h-4 w-4" />
          <AlertTitle>Event catalog not set up</AlertTitle>
          <AlertDescription>{NOT_SEEDED_MESSAGE}</AlertDescription>
        </Alert>
      ) : (
        <Card>
          <CardHeader className="mb-0 flex flex-col gap-3 md:flex-row md:items-center">
            <div className="relative w-full md:max-w-xs">
              <Search aria-hidden="true" className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
              <Input
                aria-label="Search events"
                placeholder="Search events"
                className="pl-8"
                value={filters.search}
                onChange={(event) => setFilters((current) => ({ ...current, search: event.target.value }))}
              />
            </div>
            <Select
              value={filters.serviceKey}
              onValueChange={(serviceKey) => setFilters((current) => ({ ...current, serviceKey }))}
            >
              <SelectTrigger aria-label="Service" className="w-full md:w-48">
                <SelectValue placeholder="All services" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL_SERVICES}>All services</SelectItem>
                {serviceKeys.map((serviceKey) => (
                  <SelectItem key={serviceKey} value={serviceKey}>
                    {serviceKey}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <div className="flex flex-wrap gap-2">
              <Button
                size="sm"
                variant={filters.workflowOnly ? "default" : "outline"}
                aria-pressed={filters.workflowOnly}
                onClick={() => setFilters((current) => ({ ...current, workflowOnly: !current.workflowOnly }))}
              >
                Enabled for workflows
              </Button>
              <Button
                size="sm"
                variant={filters.functionOnly ? "default" : "outline"}
                aria-pressed={filters.functionOnly}
                onClick={() => setFilters((current) => ({ ...current, functionOnly: !current.functionOnly }))}
              >
                Enabled for functions
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            {!isLoading && visible.length === 0 ? (
              <p className="py-12 text-center text-sm text-muted-foreground">
                {isFiltered || items.length > 0 ? "No events match your filters." : "The catalog has no events."}
              </p>
            ) : (
              <EventTriggerList
                items={visible}
                isLoading={isLoading}
                canManage={canManage}
                pending={pending}
                onToggle={handleToggle}
              />
            )}
          </CardContent>
        </Card>
      )}

      <Dialog open={pendingDisable !== null} onOpenChange={(open) => !open && setPendingDisable(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Disable {pendingDisable?.item.label} for workflows?</DialogTitle>
            <DialogDescription>
              {pendingDisable?.count} published workflow(s) use this event and will stop running until it is enabled
              again.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setPendingDisable(null)}>
              Cancel
            </Button>
            <Button variant="destructive" onClick={confirmDisable}>
              Disable
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  );
};

import { useState } from "react";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { pendingKey } from "../components/event-trigger-list/event-trigger-list";
import { eventTriggersService } from "../services/event-triggers.service";
import { BlocksEventFlag, IBlocksEventCatalogItem } from "../types/event-triggers.type";
import { useUpdateEventTrigger } from "./use-event-triggers-api";
import { useT } from "@seliseblocks/genesis-os";

export type PendingDisable = { item: IBlocksEventCatalogItem; count: number };

const flagTarget = (flag: BlocksEventFlag) =>
  flag === "isValidForWorkflow" ? "USE_EVENT_TRIGGER_TOGGLE.TARGET_WORKFLOWS" : "USE_EVENT_TRIGGER_TOGGLE.TARGET_FUNCTIONS";

/**
 * Saving one switch at a time per row: the switch shows its new value while the save is in flight (and
 * reverts on failure), and turning Workflow off for an event that published workflows use asks first.
 */
export const useEventTriggerToggle = (canManage: boolean) => {
  const { t } = useT("logic.event-triggers");
  const { mutateAsync } = useUpdateEventTrigger();
  const [pending, setPending] = useState<ReadonlySet<string>>(new Set());
  // The value a switch shows while its save is in flight; dropped on failure, which reverts the switch.
  const [optimistic, setOptimistic] = useState<Record<string, boolean>>({});
  const [pendingDisable, setPendingDisable] = useState<PendingDisable | null>(null);

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
        showErrorToast({ title: t("USE_EVENT_TRIGGER_TOGGLE.COULD_NOT_UPDATE", { label: item.label }), errors: result.errors ?? t("USE_EVENT_TRIGGER_TOGGLE.SOMETHING_WENT_WRONG") });
        return;
      }
      showSuccessToast({
        description: t(next ? "USE_EVENT_TRIGGER_TOGGLE.ENABLED_FOR" : "USE_EVENT_TRIGGER_TOGGLE.DISABLED_FOR", {
          label: item.label,
          target: t(flagTarget(flag)),
        }),
      });
    } catch (error) {
      showErrorToast({
        title: t("USE_EVENT_TRIGGER_TOGGLE.COULD_NOT_UPDATE", { label: item.label }),
        errors: isErrorWithErrors(error) ? error.errors : t("USE_EVENT_TRIGGER_TOGGLE.SOMETHING_WENT_WRONG"),
      });
    } finally {
      // On success the cached list already holds the new value; on failure this reverts the switch.
      dropOptimistic(key);
      setFlagPending(key, false);
    }
  };

  /** Published workflows that would stop; 0 when the count cannot be read (the save still reports real failures). */
  const usageOf = async (item: IBlocksEventCatalogItem) => {
    const key = pendingKey(item.eventName, "isValidForWorkflow");
    setFlagPending(key, true);
    try {
      return (await eventTriggersService.getUsage(item.eventName)).publishedWorkflowCount;
    } catch {
      return 0;
    } finally {
      setFlagPending(key, false);
    }
  };

  const handleToggle = async (item: IBlocksEventCatalogItem, flag: BlocksEventFlag, next: boolean) => {
    if (!canManage) return;
    if (flag === "isValidForWorkflow" && !next) {
      // Turning Workflow off stops published workflows; say how many first.
      const count = await usageOf(item);
      if (count > 0) {
        setPendingDisable({ item, count });
        return;
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

  return {
    pending,
    optimistic,
    pendingDisable,
    cancelDisable: () => setPendingDisable(null),
    handleToggle,
    confirmDisable,
  };
};

import { IBlocksEventCatalogItem } from "../types/event-triggers.type";

export const ALL_SERVICES = "__all__";

export interface EventTriggerFilters {
  search: string;
  serviceKey: string;
  workflowOnly: boolean;
  functionOnly: boolean;
}

export const DEFAULT_EVENT_TRIGGER_FILTERS: EventTriggerFilters = {
  search: "",
  serviceKey: ALL_SERVICES,
  workflowOnly: false,
  functionOnly: false,
};

/** Client-side filtering: by Label or EventName text, by service, and by the "Enabled for…" chips. */
export const filterEvents = (items: IBlocksEventCatalogItem[], filters: EventTriggerFilters) => {
  const search = filters.search.trim().toLowerCase();
  return items.filter(
    (item) =>
      (!search ||
        item.label.toLowerCase().includes(search) ||
        item.eventName.toLowerCase().includes(search)) &&
      (filters.serviceKey === ALL_SERVICES || item.serviceKey === filters.serviceKey) &&
      (!filters.workflowOnly || item.isValidForWorkflow) &&
      (!filters.functionOnly || item.isValidForFunction),
  );
};

/** The distinct service keys, for the service filter. */
export const serviceKeysOf = (items: IBlocksEventCatalogItem[]) =>
  [...new Set(items.map((item) => item.serviceKey))].sort((a, b) => a.localeCompare(b));

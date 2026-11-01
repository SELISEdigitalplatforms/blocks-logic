import { Search } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { ALL_SERVICES, EventTriggerFilters } from "../../utils/filter-events";
import { useT } from "@seliseblocks/genesis-os";

type EventTriggerToolbarProps = {
  filters: EventTriggerFilters;
  serviceKeys: string[];
  onChange: (update: (current: EventTriggerFilters) => EventTriggerFilters) => void;
};

/** Search, service filter and the "Enabled for…" chips. Filtering happens on the client. */
export const EventTriggerToolbar = ({ filters, serviceKeys, onChange }: EventTriggerToolbarProps) => {
  const { t } = useT("logic.event-triggers");
  return (
    <>
      <div className="relative w-full md:max-w-xs">
        <Search aria-hidden="true" className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
        <Input
          aria-label={t("EVENT_TRIGGER_TOOLBAR.SEARCH_EVENTS")}
          placeholder={t("EVENT_TRIGGER_TOOLBAR.SEARCH_EVENTS")}
          className="pl-8"
          value={filters.search}
          onChange={(event) => onChange((current) => ({ ...current, search: event.target.value }))}
        />
      </div>
      <Select value={filters.serviceKey} onValueChange={(serviceKey) => onChange((current) => ({ ...current, serviceKey }))}>
        <SelectTrigger aria-label={t("EVENT_TRIGGER_TOOLBAR.SERVICE")} className="w-full md:w-48">
          <SelectValue placeholder={t("EVENT_TRIGGER_TOOLBAR.ALL_SERVICES")} />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={ALL_SERVICES}>{t("EVENT_TRIGGER_TOOLBAR.ALL_SERVICES")}</SelectItem>
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
          onClick={() => onChange((current) => ({ ...current, workflowOnly: !current.workflowOnly }))}
        >
          {t("EVENT_TRIGGER_TOOLBAR.ENABLED_FOR_WORKFLOWS")}
        </Button>
        <Button
          size="sm"
          variant={filters.functionOnly ? "default" : "outline"}
          aria-pressed={filters.functionOnly}
          onClick={() => onChange((current) => ({ ...current, functionOnly: !current.functionOnly }))}
        >
          {t("EVENT_TRIGGER_TOOLBAR.ENABLED_FOR_FUNCTIONS")}
        </Button>
      </div>
    </>
  );
};

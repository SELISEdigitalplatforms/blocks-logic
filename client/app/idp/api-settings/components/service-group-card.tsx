import { useState } from "react";
import { ChevronDown } from "lucide-react";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Badge } from "@/components/ui-kits/badge/badge";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui-kits/collapsible/collapsible";
import { cn } from "@/lib/utils";
import { EndpointRow } from "./endpoint-row";
import { SecurityPresetsPopover } from "./security-presets-popover";
import { IApiEndpoint } from "../models/api-endpoint.model";
import { SERVICE_META, DEFAULT_SERVICE_META } from "../constants/endpoint.constant";

type ServiceGroupCardProps = {
  service: string;
  endpoints: IApiEndpoint[];
  selectedIds: Set<string>;
  onSelectEndpoint: (id: string, checked: boolean) => void;
  onSelectGroup: (ids: string[], checked: boolean) => void;
  onToggleMfa: (endpoint: IApiEndpoint, value: boolean) => void;
  onToggleCaptcha: (endpoint: IApiEndpoint, value: boolean) => void;
  onBulkGroupMfa: (ids: string[], value: boolean) => void;
  onBulkGroupCaptcha: (ids: string[], value: boolean) => void;
};

export const ServiceGroupCard = ({
  service,
  endpoints,
  selectedIds,
  onSelectEndpoint,
  onSelectGroup,
  onToggleMfa,
  onToggleCaptcha,
  onBulkGroupMfa,
  onBulkGroupCaptcha,
}: ServiceGroupCardProps) => {
  const [open, setOpen] = useState(false);
  const meta = SERVICE_META[service] || DEFAULT_SERVICE_META;

  const groupIds = endpoints.map((e) => e.itemId);
  const selectedCount = groupIds.filter((id) => selectedIds.has(id)).length;
  const allSelected = selectedCount === groupIds.length && groupIds.length > 0;
  const someSelected = selectedCount > 0 && !allSelected;

  const handleGroupCheckbox = (checked: boolean) => {
    onSelectGroup(groupIds, checked);
  };

  return (
    <Collapsible open={open} onOpenChange={setOpen}>
      <div className="rounded-lg border border-border bg-card">
        {/* Header */}
        <div className="flex items-center gap-3 px-4 py-3">
          <Checkbox
            checked={allSelected}
            // @ts-expect-error indeterminate is supported by radix but not typed
            indeterminate={someSelected}
            onCheckedChange={handleGroupCheckbox}
            onClick={(e) => e.stopPropagation()}
          />

          <CollapsibleTrigger asChild>
            <button className="flex flex-1 items-center gap-3 text-left">
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <h3 className="text-base font-semibold">{service} API</h3>
                </div>
                <p className="truncate text-sm text-muted-foreground">{meta.description}</p>
              </div>
            </button>
          </CollapsibleTrigger>

          <div className="flex shrink-0 items-center gap-2">
            <Badge variant="outline" className="rounded-full font-mono text-xs">
              {endpoints.length} Endpoint{endpoints.length !== 1 ? "s" : ""}
            </Badge>
            <SecurityPresetsPopover
              onEnableAllMfa={() => onBulkGroupMfa(groupIds, true)}
              onEnableAllCaptcha={() => onBulkGroupCaptcha(groupIds, true)}
            />
            <CollapsibleTrigger asChild>
              <button className="rounded-md p-1 hover:bg-accent">
                <ChevronDown
                  className={cn(
                    "h-4 w-4 text-muted-foreground transition-transform duration-200",
                    open && "rotate-180",
                  )}
                />
              </button>
            </CollapsibleTrigger>
          </div>
        </div>

        {/* Expanded endpoint list */}
        <CollapsibleContent>
          <div className="flex flex-col gap-2 border-t border-border px-4 py-3">
            {endpoints.map((ep) => (
              <EndpointRow
                key={ep.itemId}
                endpoint={ep}
                isSelected={selectedIds.has(ep.itemId)}
                onSelect={onSelectEndpoint}
                onToggleMfa={onToggleMfa}
                onToggleCaptcha={onToggleCaptcha}
              />
            ))}
          </div>
        </CollapsibleContent>
      </div>
    </Collapsible>
  );
};

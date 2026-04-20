import { useState } from "react";
import { ChevronDown } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui-kits/collapsible/collapsible";
import { cn } from "@/lib/utils";
import { EndpointRow } from "./endpoint-row";
import { IApiEndpoint } from "../models/api-endpoint.model";
import { SERVICE_META, DEFAULT_SERVICE_META } from "../constants/endpoint.constant";

type ServiceGroupCardProps = {
  service: string;
  endpoints: IApiEndpoint[];
  onToggleMfa: (endpoint: IApiEndpoint, value: boolean) => void;
  onToggleCaptcha: (endpoint: IApiEndpoint, value: boolean) => void;
};

export const ServiceGroupCard = ({
  service,
  endpoints,
  onToggleMfa,
  onToggleCaptcha,
}: ServiceGroupCardProps) => {
  const [open, setOpen] = useState(false);
  const meta = SERVICE_META[service] || DEFAULT_SERVICE_META;

  return (
    <Collapsible open={open} onOpenChange={setOpen}>
      <div className="rounded-lg border border-border bg-card">
        {/* Header */}
        <CollapsibleTrigger asChild>
          <div className="flex cursor-pointer items-center gap-3 px-4 py-3">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-2">
                <h3 className="text-base font-semibold">{service} API</h3>
              </div>
              <p className="truncate text-sm text-muted-foreground">{meta.description}</p>
            </div>

            <div className="flex shrink-0 items-center gap-2">
              <Badge variant="outline" className="rounded-full font-mono text-xs">
                {endpoints.length} Endpoint{endpoints.length !== 1 ? "s" : ""}
              </Badge>
              <ChevronDown
                className={cn(
                  "h-4 w-4 text-muted-foreground transition-transform duration-200",
                  open && "rotate-180",
                )}
              />
            </div>
          </div>
        </CollapsibleTrigger>

        {/* Expanded endpoint list */}
        <CollapsibleContent>
          <div className="flex flex-col gap-2 border-t border-border px-4 py-3">
            {endpoints.map((ep) => (
              <EndpointRow
                key={ep.itemId}
                endpoint={ep}
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

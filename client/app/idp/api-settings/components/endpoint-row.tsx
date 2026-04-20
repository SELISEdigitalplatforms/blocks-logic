import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Switch } from "@/components/ui-kits/switch/switch";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Lock } from "lucide-react";
import { MethodBadge } from "./method-badge";
import { IApiEndpoint } from "../models/api-endpoint.model";

type EndpointRowProps = {
  endpoint: IApiEndpoint;
  isSelected: boolean;
  onSelect: (id: string, checked: boolean) => void;
  onToggleMfa: (endpoint: IApiEndpoint, value: boolean) => void;
  onToggleCaptcha: (endpoint: IApiEndpoint, value: boolean) => void;
};

export const EndpointRow = ({
  endpoint,
  isSelected,
  onSelect,
  onToggleMfa,
  onToggleCaptcha,
}: EndpointRowProps) => {
  const isCritical = endpoint.method.toUpperCase() === "DELETE";

  return (
    <div className="flex flex-col gap-3 rounded-md border border-border bg-card px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
      {/* Left: checkbox + method + path + description */}
      <div className="flex items-center gap-3 min-w-0 flex-1">
        <Checkbox
          checked={isSelected}
          onCheckedChange={(checked) => onSelect(endpoint.itemId, !!checked)}
        />
        <MethodBadge method={endpoint.method} />
        <code className="truncate rounded bg-muted px-2 py-0.5 text-sm font-medium">
          {endpoint.endpoint}
        </code>
        {isCritical && (
          <Badge variant="error" className="shrink-0 text-[10px] uppercase">
            Critical
          </Badge>
        )}
      </div>

      {/* Description (hidden on small screens, shown below on mobile) */}
      <p className="text-sm text-muted-foreground sm:hidden pl-8">{endpoint.description}</p>

      {/* Right: toggles */}
      <div className="flex items-center gap-6 shrink-0 pl-8 sm:pl-0">
        <div className="flex items-center gap-2">
          <span className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
            MFA Required
          </span>
          <Switch
            size="sm"
            checked={endpoint.isMfaRequired}
            onCheckedChange={(val) => onToggleMfa(endpoint, val)}
          />
          {endpoint.isMfaRequired && (
            <Lock className="h-3.5 w-3.5 text-amber-500" />
          )}
        </div>

        <div className="flex items-center gap-2">
          <span className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
            Captcha
          </span>
          <Switch
            size="sm"
            checked={endpoint.isCaptchaRequired}
            onCheckedChange={(val) => onToggleCaptcha(endpoint, val)}
          />
        </div>
      </div>
    </div>
  );
};

/** Desktop-only description row underneath the endpoint row */
export const EndpointDescription = ({ description }: { description: string }) => (
  <p className="hidden sm:block pl-[120px] -mt-1 pb-1 text-sm text-muted-foreground">
    {description}
  </p>
);

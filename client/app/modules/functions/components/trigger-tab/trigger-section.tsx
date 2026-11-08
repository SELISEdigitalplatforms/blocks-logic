import type { ReactNode } from "react";
import { ChevronDown, type LucideIcon } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card } from "@/components/ui-kits/card/card";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui-kits/collapsible/collapsible";
import { Switch } from "@/components/ui-kits/switch/switch";

type TriggerSectionProps = {
  icon: LucideIcon;
  title: string;
  subtitle: string;
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
  /** Names the on/off switch for screen readers. */
  switchLabel: string;
  /** Names the chevron button that opens and closes the body. */
  toggleLabel: string;
  /** Extra header control, shown before the switch (for example an info button). */
  headerExtra?: ReactNode;
  /** The switch cannot change yet (the saved value is still loading). */
  disabled?: boolean;
  /** Set both to control the body from outside; leave both out and it starts closed. */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  children: ReactNode;
};

/**
 * One way to start a function. Every section has the same header: icon, title and subtitle, the
 * on/off switch, then a chevron that opens the body. The body is closed until asked for.
 */
export const TriggerSection = ({
  icon: Icon,
  title,
  subtitle,
  checked,
  onCheckedChange,
  switchLabel,
  toggleLabel,
  headerExtra,
  disabled,
  open,
  onOpenChange,
  children,
}: Readonly<TriggerSectionProps>) => (
  <Card className="rounded-xl">
    <Collapsible open={open} onOpenChange={onOpenChange} defaultOpen={false}>
      <div className="flex items-center gap-3 p-4">
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-muted text-primary">
          <Icon className="h-[18px] w-[18px]" aria-hidden="true" />
        </span>
        <div className="flex min-w-0 flex-1 flex-col">
          <h2 className="text-sm font-semibold">{title}</h2>
          <span className="text-xs text-medium-emphasis">{subtitle}</span>
        </div>
        {headerExtra}
        <Switch
          aria-label={switchLabel}
          checked={checked}
          disabled={disabled}
          onCheckedChange={onCheckedChange}
          className="shrink-0"
        />
        <CollapsibleTrigger asChild>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            aria-label={toggleLabel}
            className="h-8 w-8 shrink-0 [&[data-state=open]>svg]:rotate-180"
          >
            <ChevronDown className="h-[18px] w-[18px] transition-transform" aria-hidden="true" />
          </Button>
        </CollapsibleTrigger>
      </div>
      <CollapsibleContent className="border-t p-4">{children}</CollapsibleContent>
    </Collapsible>
  </Card>
);

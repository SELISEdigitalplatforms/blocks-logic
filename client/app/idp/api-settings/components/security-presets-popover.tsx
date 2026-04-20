import { Button } from "@/components/ui-kits/button/button";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui-kits/popover/popover";
import { Shield } from "lucide-react";

type SecurityPresetsPopoverProps = {
  onEnableAllMfa: () => void;
  onEnableAllCaptcha: () => void;
};

export const SecurityPresetsPopover = ({
  onEnableAllMfa,
  onEnableAllCaptcha,
}: SecurityPresetsPopoverProps) => {
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" className="gap-1.5">
          <Shield className="h-3.5 w-3.5" />
          Security Presets
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-48 p-1">
        <button
          className="w-full rounded-sm px-3 py-2 text-left text-sm hover:bg-accent"
          onClick={onEnableAllMfa}
        >
          Enable all MFA
        </button>
        <button
          className="w-full rounded-sm px-3 py-2 text-left text-sm hover:bg-accent"
          onClick={onEnableAllCaptcha}
        >
          Enable all Captcha
        </button>
      </PopoverContent>
    </Popover>
  );
};

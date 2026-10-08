import { useState } from "react";
import { ChevronDown } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui-kits/collapsible/collapsible";
import { cn } from "@/lib/utils";
import { SANDBOX_CODE_RULES } from "../../constants/limits.constant";

/**
 * "Don't / Do instead" beside the editor, so the reuse rules are read while writing the code, not
 * only on the Trigger tab. Reuse is always on for HTTP calls of a deployed function (2026-10-06),
 * so the card is open by default: breaking a rule replaces the sandbox and can stop unfinished work.
 */
export const SandboxRulesCard = () => {
  const [isOpen, setIsOpen] = useState(true);

  return (
    <Card>
      <Collapsible open={isOpen} onOpenChange={setIsOpen}>
        <CollapsibleTrigger className="flex w-full items-center justify-between gap-3 p-4 text-left">
          <span className="text-sm font-semibold">What not to do, and what to do instead</span>
          <ChevronDown
            className={cn(
              "h-4 w-4 shrink-0 text-medium-emphasis transition-transform",
              isOpen && "rotate-180",
            )}
          />
        </CollapsibleTrigger>
        <CollapsibleContent>
          <CardContent className="flex flex-col gap-3 px-4 pb-4 pt-0">
            <p
              className="rounded-md bg-surface-app p-2.5 text-xs leading-relaxed text-medium-emphasis"
              data-testid="sandbox-rules-mode"
            >
              <strong className="font-semibold">Calls of a deployed function reuse the sandbox.</strong> A call that
              breaks one of these keeps its result, but its sandbox is replaced after it and work it
              left running may be stopped. The run shows &quot;Sandbox replaced after this call&quot;.
            </p>
            <ol className="flex flex-col gap-3" data-testid="sandbox-rules">
              {SANDBOX_CODE_RULES.map((rule) => (
                <li
                  key={rule.dont}
                  className="flex flex-col gap-1.5 border-t pt-3 first:border-t-0 first:pt-0"
                >
                  <span className="text-xs leading-relaxed">
                    <strong className="font-semibold text-error">Don&apos;t:</strong> {rule.dont}
                  </span>
                  <pre className="overflow-x-auto whitespace-pre rounded-md bg-surface-app p-2 font-mono text-[11px] leading-relaxed text-medium-emphasis">
                    {rule.dontCode}
                  </pre>
                  <span className="text-xs leading-relaxed">
                    <strong className="font-semibold text-success">Do:</strong> {rule.fix}
                  </span>
                  <pre className="overflow-x-auto whitespace-pre rounded-md bg-surface-app p-2 font-mono text-[11px] leading-relaxed text-medium-emphasis">
                    {rule.fixCode}
                  </pre>
                </li>
              ))}
            </ol>
          </CardContent>
        </CollapsibleContent>
      </Collapsible>
    </Card>
  );
};

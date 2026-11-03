import { useState } from "react";
import { ChevronDown } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui-kits/collapsible/collapsible";
import { cn } from "@/lib/utils";
import { SANDBOX_CTX_DOCS } from "../../constants/limits.constant";
import { IFunctionLimits } from "../../types/function.types";
import { renderRichText } from "@/lib/rich-text";
import { useT } from "@seliseblocks/genesis-os";

type SandboxHelpCardProps = {
  limits: IFunctionLimits;
};

/** The `handler(input, ctx)` contract, as a collapsible rail reference rather than a page banner. */
export const SandboxHelpCard = ({ limits }: SandboxHelpCardProps) => {
  const { t } = useT("logic.functions.components");
  const [isOpen, setIsOpen] = useState(true);

  return (
    <Card>
      <Collapsible open={isOpen} onOpenChange={setIsOpen}>
        <CollapsibleTrigger className="flex w-full items-center justify-between gap-3 p-4 text-left">
          <span className="text-sm font-semibold">
            {t("SANDBOX_HELP_CARD.WHAT_THE_SANDBOX_GIVES")}
          </span>
          <ChevronDown
            className={cn(
              "h-4 w-4 shrink-0 text-medium-emphasis transition-transform",
              isOpen && "rotate-180",
            )}
          />
        </CollapsibleTrigger>
        <CollapsibleContent>
          <CardContent className="flex flex-col gap-2.5 px-4 pb-4 pt-0">
            <p className="text-xs leading-relaxed text-medium-emphasis">
              {renderRichText(t("SANDBOX_HELP_CARD.EXPORT_A_DEFAULT_ASYNC"), [
                (children) => <code className="font-mono">{children}</code>,
              ])}
            </p>
            {/* The first thing people get wrong: `ctx` at module scope, which fails the run before
                the handler is reached. Saying where top-level code IS useful stops the correction
                from going too far the other way and moving warm state inside the handler. */}
            <p className="rounded-md bg-surface-app p-2.5 text-xs leading-relaxed text-medium-emphasis">
              {renderRichText(t("SANDBOX_HELP_CARD.N_0_INPUT_0_AND"), [
                (children) => <code className="font-mono">{children}</code>,
                (children) => <code className="font-mono">{children}</code>,
                (children) => <strong className="font-semibold">{children}</strong>,
              ])}
            </p>
            {SANDBOX_CTX_DOCS.map((doc) => (
              <div key={doc.name} className="flex flex-col gap-0.5">
                <code className="font-mono text-xs font-semibold text-primary">{doc.name}</code>
                <span className="text-xs leading-relaxed text-medium-emphasis">
                  {doc.description}
                </span>
              </div>
            ))}
            <p className="border-t pt-2.5 text-xs leading-relaxed text-low-emphasis">
              {renderRichText(
                t("SANDBOX_HELP_CARD.THIS_FUNCTION_RUNS_WITH", {
                  cpuMillicores: limits.cpuMillicores,
                }),
                [(children) => <code className="font-mono">{children}</code>],
              )}
            </p>
          </CardContent>
        </CollapsibleContent>
      </Collapsible>
    </Card>
  );
};

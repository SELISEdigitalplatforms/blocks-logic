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

type SandboxHelpCardProps = {
  limits: IFunctionLimits;
};

/** The `handler(input, ctx)` contract, as a collapsible rail reference rather than a page banner. */
export const SandboxHelpCard = ({ limits }: SandboxHelpCardProps) => {
  const [isOpen, setIsOpen] = useState(true);

  return (
    <Card>
      <Collapsible open={isOpen} onOpenChange={setIsOpen}>
        <CollapsibleTrigger className="flex w-full items-center justify-between gap-3 p-4 text-left">
          <span className="text-sm font-semibold">What the sandbox gives you</span>
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
              Export a default async function:{" "}
              <code className="font-mono">export default async function handler(input, ctx)</code>.
              Whatever it returns becomes the run&apos;s result.
            </p>
            {/* The first thing people get wrong: `ctx` at module scope, which fails the run before
                the handler is reached. Saying where top-level code IS useful stops the correction
                from going too far the other way and moving warm state inside the handler. */}
            <p className="rounded-md bg-surface-app p-2.5 text-xs leading-relaxed text-medium-emphasis">
              <code className="font-mono">input</code> and <code className="font-mono">ctx</code>{" "}
              are its <strong className="font-semibold">parameters</strong> — they exist only inside
              the handler. Code at the top of the file runs once when the sandbox starts, so put
              clients, caches and constants there; anything that reads the request or the context
              goes inside.
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
              This function runs with {limits.memoryMb} MB, {limits.cpuMillicores}m of CPU and a{" "}
              {limits.timeoutSeconds} s timeout. Node 24, read-only filesystem apart from{" "}
              <code className="font-mono">/tmp</code>.
            </p>
          </CardContent>
        </CollapsibleContent>
      </Collapsible>
    </Card>
  );
};

import { ChevronDown } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui-kits/collapsible/collapsible";
import { ExecutionSection, SectionNote } from "./execution-section";
import { ReadOnlyCodeViewer } from "./read-only-code-viewer";

interface ExecutionRawSnapshotSectionProps {
  /** Pretty JSON, or the raw string when it does not parse. */
  value: string;
  /** True when `value` is not valid JSON: shown as plain text, expanded. */
  invalid?: boolean;
}

export const ExecutionRawSnapshotSection = ({ value, invalid = false }: Readonly<ExecutionRawSnapshotSectionProps>) => (
  <ExecutionSection title="Raw snapshot">
    <Collapsible defaultOpen={invalid}>
      <CollapsibleTrigger asChild>
        <Button type="button" variant="outline" size="xs" className="group gap-1.5">
          Raw snapshot (JSON)
          <ChevronDown className="h-3.5 w-3.5 transition-transform group-data-[state=open]:rotate-180" aria-hidden />
        </Button>
      </CollapsibleTrigger>
      <CollapsibleContent className="mt-3 space-y-2">
        <ReadOnlyCodeViewer value={value} label="Snapshot" language={invalid ? "plaintext" : "json"} />
        <SectionNote>Secrets are redacted.</SectionNote>
      </CollapsibleContent>
    </Collapsible>
  </ExecutionSection>
);

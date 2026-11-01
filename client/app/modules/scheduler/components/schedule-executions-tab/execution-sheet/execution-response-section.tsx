import { isSchedulerRecorded, isTruncated } from "../status-label";
import { StatusChip } from "../status-chip";
import { ExecutionSection, SectionNote } from "./execution-section";
import { ReadOnlyCodeViewer } from "./read-only-code-viewer";

interface ExecutionResponseSectionProps {
  body?: string | null;
  statusLabel: string;
}

export const ExecutionResponseSection = ({ body, statusLabel }: Readonly<ExecutionResponseSectionProps>) => (
  <ExecutionSection title="Response">
    <StatusChip label={statusLabel} />
    {body ? (
      <>
        {isSchedulerRecorded(body) && (
          <SectionNote>Recorded by the scheduler — no response from the target.</SectionNote>
        )}
        <ReadOnlyCodeViewer value={body} label="Response body" />
        {isTruncated(body) && <SectionNote>Body truncated to 16 KB.</SectionNote>}
      </>
    ) : (
      <SectionNote>No body.</SectionNote>
    )}
  </ExecutionSection>
);

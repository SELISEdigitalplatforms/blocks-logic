import { isSchedulerRecorded, isTruncated } from "../status-label";
import { StatusChip } from "../status-chip";
import { ExecutionSection, SectionNote } from "./execution-section";
import { ReadOnlyCodeViewer } from "./read-only-code-viewer";
import { useT } from "@seliseblocks/genesis-os";

interface ExecutionResponseSectionProps {
  body?: string | null;
  statusLabel: string;
}

export const ExecutionResponseSection = ({ body, statusLabel }: Readonly<ExecutionResponseSectionProps>) => {
  const { t } = useT("logic.scheduler");
  return (
    <ExecutionSection title={t("EXECUTION_RESPONSE_SECTION.RESPONSE")}>
      <StatusChip label={statusLabel} />
      {body ? (
        <>
          {isSchedulerRecorded(body) && (
            <SectionNote>{t("EXECUTION_RESPONSE_SECTION.RECORDED_BY_THE_SCHEDULER")}</SectionNote>
          )}
          <ReadOnlyCodeViewer value={body} label={t("EXECUTION_RESPONSE_SECTION.RESPONSE_BODY")} />
          {isTruncated(body) && <SectionNote>{t("EXECUTION_RESPONSE_SECTION.BODY_TRUNCATED_TO_16")}</SectionNote>}
        </>
      ) : (
        <SectionNote>{t("EXECUTION_RESPONSE_SECTION.NO_BODY")}</SectionNote>
      )}
    </ExecutionSection>
  );
};

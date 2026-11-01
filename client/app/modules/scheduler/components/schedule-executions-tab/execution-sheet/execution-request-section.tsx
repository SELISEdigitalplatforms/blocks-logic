import { Badge } from "@/components/ui-kits/badge/badge";
import { ScheduleTriggerType } from "../../../types/schedule.service.type";
import { ScheduleSnapshot } from "../parse-snapshot";
import { CopyIconButton } from "../copy-icon-button";
import { ExecutionSection, SectionNote } from "./execution-section";
import { ReadOnlyCodeViewer } from "./read-only-code-viewer";

const TRIGGER_LABELS: Record<number, string> = {
  [ScheduleTriggerType.Webhook]: "Webhook",
  [ScheduleTriggerType.Queue]: "Queue",
};

const Label = ({ children }: Readonly<{ children: string }>) => (
  <span className="text-xs font-medium text-muted-foreground">{children}</span>
);

const WebhookDetails = ({ webhook }: Readonly<{ webhook: NonNullable<ScheduleSnapshot["webhook"]> }>) => (
  <>
    <div className="space-y-1">
      <Label>Endpoint</Label>
      <div className="flex items-center gap-2 rounded-lg border bg-muted/20 px-3 py-2">
        <Badge variant="outline" className="font-mono text-xs uppercase">
          {webhook.method || "—"}
        </Badge>
        <span className="min-w-0 flex-1 break-all font-mono text-xs">{webhook.url || "—"}</span>
        {webhook.url && <CopyIconButton value={webhook.url} label="Webhook URL" />}
      </div>
    </div>
    <div className="space-y-1">
      <Label>Headers</Label>
      {webhook.headerNames.length === 0 ? (
        <SectionNote>No custom headers</SectionNote>
      ) : (
        <div className="space-y-1.5">
          {webhook.headerNames.map((name) => (
            <div
              key={name}
              className="flex items-center justify-between gap-2 rounded-md border bg-muted/20 px-3 py-1.5 font-mono text-xs"
            >
              <span className="break-all font-semibold text-foreground">{name}</span>
              <span className="text-muted-foreground">[redacted]</span>
            </div>
          ))}
        </div>
      )}
    </div>
    <p className="text-sm">{webhook.signed ? "Signed (x-signature-sha256)" : "Not signed"}</p>
  </>
);

export const ExecutionRequestSection = ({ snapshot }: Readonly<{ snapshot: ScheduleSnapshot }>) => {
  const triggerLabel = TRIGGER_LABELS[snapshot.triggerType ?? -1] ?? "—";
  const isQueue = snapshot.triggerType === ScheduleTriggerType.Queue;

  return (
    <ExecutionSection title="Request sent">
      <p className="text-sm">
        <span className="text-muted-foreground">Trigger type: </span>
        {triggerLabel}
      </p>
      {!isQueue && snapshot.webhook && <WebhookDetails webhook={snapshot.webhook} />}
      {isQueue && (
        <p className="text-sm">
          <span className="text-muted-foreground">Queue: </span>
          <span className="break-all font-mono text-xs">{snapshot.queueName || "—"}</span>
        </p>
      )}
      <div className="space-y-1">
        <Label>Payload</Label>
        {snapshot.payload ? (
          <ReadOnlyCodeViewer value={snapshot.payload} label="Payload" />
        ) : (
          <SectionNote>No payload</SectionNote>
        )}
      </div>
    </ExecutionSection>
  );
};

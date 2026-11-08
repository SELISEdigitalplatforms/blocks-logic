import { TriangleAlert } from "lucide-react";
import { useT } from "@seliseblocks/genesis-os";
import { useGetEventSettings } from "../../hooks/use-events";
import { ITriggerConfig } from "../../types/function.types";
import { ActionsSection } from "./actions-section";
import { SandboxReuseNote } from "./sandbox-reuse-note";
import { WebRequestSection } from "./web-request-section";
import { WorkflowSection } from "./workflow-section";

type TriggerTabProps = {
  functionId: string;
  trigger: ITriggerConfig;
  onTriggerChange: (value: ITriggerConfig) => void;
  requestsPerMinute: number | null | undefined;
  onRequestsPerMinuteChange: (value: number | null) => void;
};

/**
 * The Trigger tab: three ways to start the function (web request, actions, workflow step), each a
 * section with its own switch, all closed until opened. Saving stays possible when all three are
 * off — the function just cannot start, and the warning says so.
 */
export const TriggerTab = ({
  functionId,
  trigger,
  onTriggerChange,
  requestsPerMinute,
  onRequestsPerMinuteChange,
}: Readonly<TriggerTabProps>) => {
  const { t } = useT("logic.functions.components");
  const { data: eventSettings } = useGetEventSettings(functionId);
  // Until the saved value arrives (or if an older server sends none), actions count as on.
  const actionsEnabled = eventSettings?.actionsEnabled ?? true;
  const cannotStart = !trigger.httpEnabled && !actionsEnabled && !trigger.workflowEnabled;

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-medium-emphasis">{t("TRIGGER_TAB.INTRO")}</p>
      {cannotStart && (
        <p
          role="alert"
          className="flex items-center gap-2 rounded-lg border border-warning-100 bg-warning-50 px-3 py-2 text-sm text-warning-800"
        >
          <TriangleAlert className="h-4 w-4 shrink-0" aria-hidden="true" />
          <span>{t("TRIGGER_TAB.CANNOT_START")}</span>
        </p>
      )}
      <WebRequestSection
        functionId={functionId}
        trigger={trigger}
        onTriggerChange={onTriggerChange}
        requestsPerMinute={requestsPerMinute}
        onRequestsPerMinuteChange={onRequestsPerMinuteChange}
      />
      <ActionsSection functionId={functionId} />
      <WorkflowSection value={trigger} onChange={onTriggerChange} />
      <SandboxReuseNote />
    </div>
  );
};

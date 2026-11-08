import { Workflow } from "lucide-react";
import { useT } from "@seliseblocks/genesis-os";
import { ITriggerConfig } from "../../types/function.types";
import { TriggerSection } from "./trigger-section";

const LOGIC_FUNCTIONS_COMPONENTS_MODULE = "logic.functions.components";

type WorkflowSectionProps = {
  value: ITriggerConfig;
  onChange: (value: ITriggerConfig) => void;
};

const DiagramNode = ({
  label,
  highlighted,
}: Readonly<{ label: string; highlighted?: boolean }>) => (
  <span
    className={`min-w-0 flex-1 rounded-md border bg-background px-3 py-2 text-center text-xs font-medium ${
      highlighted ? "border-primary text-primary" : "text-medium-emphasis"
    }`}
  >
    {label}
  </span>
);

/** "Workflow step": lets a workflow step run this function and use its result. */
export const WorkflowSection = ({ value, onChange }: Readonly<WorkflowSectionProps>) => {
  const { t } = useT(LOGIC_FUNCTIONS_COMPONENTS_MODULE);

  return (
    <TriggerSection
      icon={Workflow}
      title={t("TRIGGER_TAB.WORKFLOW_STEP")}
      subtitle={t("TRIGGER_TAB.WORKFLOW_STEP_SUBTITLE")}
      checked={value.workflowEnabled}
      onCheckedChange={(workflowEnabled) => onChange({ ...value, workflowEnabled })}
      switchLabel={t("TRIGGER_TAB.ALLOW_IN_WORKFLOWS")}
      toggleLabel={t("TRIGGER_TAB.SHOW_OR_HIDE_WORKFLOW_STEP")}
    >
      <div className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <DiagramNode label={t("TRIGGER_TAB.STEP_BEFORE")} />
          <span className="shrink-0 text-sm text-low-emphasis" aria-hidden="true">
            →
          </span>
          <DiagramNode label={t("TRIGGER_TAB.THIS_FUNCTION")} highlighted />
          <span className="shrink-0 text-sm text-low-emphasis" aria-hidden="true">
            →
          </span>
          <DiagramNode label={t("TRIGGER_TAB.NEXT_STEP")} />
        </div>
        <span className="text-xs leading-relaxed text-medium-emphasis">
          {t("TRIGGER_TAB.WORKFLOW_WAITS_HELP")}
        </span>
      </div>
    </TriggerSection>
  );
};

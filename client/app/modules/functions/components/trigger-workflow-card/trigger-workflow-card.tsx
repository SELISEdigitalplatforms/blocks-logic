import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Switch } from "@/components/ui-kits/switch/switch";
import { ITriggerConfig } from "../../types/function.types";
import { useT, tStatic } from "@seliseblocks/genesis-os";

const LOGIC_FUNCTIONS_COMPONENTS_MODULE = "logic.functions.components";

type TriggerWorkflowCardProps = {
  value: ITriggerConfig;
  onChange: (value: ITriggerConfig) => void;
};

const NODES = [
  {
    get title() { return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TRIGGER_WORKFLOW_CARD.PREVIOUS_NODE"); },
    body: "Its output becomes this function's input",
    isFunction: false,
  },
  { get title() { return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TRIGGER_WORKFLOW_CARD.FUNCTION_NODE"); }, body: "Runs handler(input, ctx) in its sandbox", isFunction: true },
  { get title() { return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TRIGGER_WORKFLOW_CARD.NEXT_NODE"); }, body: "Receives the value the handler returns", isFunction: false },
];

export const TriggerWorkflowCard = ({ value, onChange }: TriggerWorkflowCardProps) => {
  const { t } = useT(LOGIC_FUNCTIONS_COMPONENTS_MODULE);
  return (
    <Card>
      <CardContent className="flex flex-col gap-3 p-5">
        {/* The card sits in a half-width column, so the heading block has to be allowed to shrink:
            left to its max-content width it never fits beside the switch and wraps it onto a line
            of its own. Same row idiom as the HTTP card above it. */}
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <span className="text-base font-semibold">{t("TRIGGER_WORKFLOW_CARD.USE_IN_WORKFLOWS")}</span>
            <span className="text-xs leading-relaxed text-medium-emphasis">
              {t("TRIGGER_WORKFLOW_CARD.A_WORKFLOW_NODE_CAN")}
            </span>
          </div>
          <Switch
            aria-label={t("TRIGGER_WORKFLOW_CARD.USE_IN_WORKFLOWS")}
            checked={value.workflowEnabled}
            onCheckedChange={(checked) => onChange({ ...value, workflowEnabled: checked })}
            className="flex-shrink-0"
          />
        </div>

        {value.workflowEnabled && (
          <div className="flex flex-col gap-3 rounded-lg border bg-surface-app p-4">
            <div className="flex flex-wrap items-stretch gap-2">
              {NODES.map((node, index) => (
                <div key={node.title} className="flex min-w-0 flex-1 items-center gap-2">
                  <div
                    className={`flex min-w-0 flex-1 flex-col gap-1 rounded-md border bg-background p-3 ${
                      node.isFunction ? "border-primary" : "border-border"
                    }`}
                  >
                    <span
                      className={`text-[10px] font-semibold uppercase tracking-wide ${
                        node.isFunction ? "text-primary" : "text-low-emphasis"
                      }`}
                    >
                      {node.title}
                    </span>
                    <span className="text-xs leading-relaxed text-medium-emphasis">{node.body}</span>
                  </div>
                  {index < NODES.length - 1 && (
                    <span className="shrink-0 text-sm text-low-emphasis">→</span>
                  )}
                </div>
              ))}
            </div>
            <span className="text-xs leading-relaxed text-medium-emphasis">
              {t("TRIGGER_WORKFLOW_CARD.THE_WORKFLOW_WAITS_FOR")}
            </span>
          </div>
        )}
      </CardContent>
    </Card>
  );
};

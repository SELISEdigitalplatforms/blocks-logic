import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Switch } from "@/components/ui-kits/switch/switch";
import { ITriggerConfig } from "../../types/function.types";

type TriggerWorkflowCardProps = {
  value: ITriggerConfig;
  onChange: (value: ITriggerConfig) => void;
};

const NODES = [
  {
    title: "Previous node",
    body: "Its output becomes this function's input",
    isFunction: false,
  },
  { title: "Function node", body: "Runs handler(input, ctx) in its sandbox", isFunction: true },
  { title: "Next node", body: "Receives the value the handler returns", isFunction: false },
];

export const TriggerWorkflowCard = ({ value, onChange }: TriggerWorkflowCardProps) => (
  <Card>
    <CardContent className="flex flex-col gap-3 p-5">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex min-w-0 flex-col gap-1">
          <span className="text-base font-semibold">Use in workflows</span>
          <span className="text-xs leading-relaxed text-medium-emphasis">
            A workflow node can trigger this function, and what it returns is passed to the next
            node.
          </span>
        </div>
        <Switch
          aria-label="Use in workflows"
          checked={value.workflowEnabled}
          onCheckedChange={(checked) => onChange({ ...value, workflowEnabled: checked })}
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
            The workflow waits for the run to finish, so the next node always gets the value. A
            throw fails the step and the workflow&apos;s own error path takes over.
          </span>
        </div>
      )}
    </CardContent>
  </Card>
);

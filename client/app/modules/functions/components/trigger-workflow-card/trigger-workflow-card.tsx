import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui-kits/card/card";
import { Switch } from "@/components/ui-kits/switch/switch";
import { ITriggerConfig } from "../../types/function.types";

type TriggerWorkflowCardProps = {
  value: ITriggerConfig;
  onChange: (value: ITriggerConfig) => void;
};

export const TriggerWorkflowCard = ({ value, onChange }: TriggerWorkflowCardProps) => (
  <Card>
    <CardHeader className="flex flex-row items-center justify-between pb-3">
      <div>
        <CardTitle className="text-base">Workflow trigger</CardTitle>
        <CardDescription>Invoke this function from a workflow&apos;s Function step.</CardDescription>
      </div>
      <Switch
        checked={value.workflowEnabled}
        onCheckedChange={(checked) => onChange({ ...value, workflowEnabled: checked })}
      />
    </CardHeader>
    {value.workflowEnabled && (
      <CardContent>
        <p className="text-sm text-muted-foreground">
          Once deployed, this function appears in the Function step&apos;s function picker for every
          workflow in this project.
        </p>
      </CardContent>
    )}
  </Card>
);

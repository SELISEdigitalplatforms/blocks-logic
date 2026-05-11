import { Card, CardContent } from "@/components/ui-kits/card/card";
import { AddWorkflow } from "../../components/add-workflow";

export const NoWorkflows = () => {
  return (
    <Card className="border-none bg-transparent p-0 shadow-none">
      <CardContent>
        <p className="text-base leading-relaxed">
          Automate your processes with Workflows — design, configure, and deploy automation flows
          with ease.
        </p>

        <div className="mt-6 space-y-2">
          <p className="font-medium">To get started:</p>
          <ol className="ml-2 list-inside list-decimal space-y-1">
            <li>Click Add Workflow</li>
            <li>Set a name and description for your workflow</li>
            <li>Configure triggers, actions, and conditions</li>
            <li>Save and activate your workflow to begin automation</li>
          </ol>
        </div>

        <div className="mt-8">
          <AddWorkflow />
        </div>
      </CardContent>
    </Card>
  );
};

import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { NodeInspectorHeader } from "../node-inspector-header";
import { useState } from "react";
import { NodeSchema } from "../../node-schemas/node-schema.type";
import { FormBuilder } from "../form-builder/form-builder";
import { useWorkflow } from "@blocks-workflow/hooks";
import { TestEventPanel } from "../shared";

type LayoutWithTestProps = {
  schema: NodeSchema | null;
};

export const LayoutWithTest = ({ schema }: LayoutWithTestProps) => {
  const { selectedNode, updateNode } = useWorkflow();
  const [activeTab, setActiveTab] = useState("parameters");
  if (!selectedNode || !schema) return null;

  return (
    <>
      <NodeInspectorHeader />
      <div className="mt-6 flex gap-6">
        <div className="w-5/12">
          <Tabs value={activeTab} onValueChange={setActiveTab} className="h-full">
            <TabsList className="mb-5">
              <TabsTrigger value="parameters">Parameters</TabsTrigger>
              <TabsTrigger value="settings">Settings</TabsTrigger>
            </TabsList>

            <TabsContent value="parameters">
              <FormBuilder
                fields={schema.parameters}
                data={selectedNode.parameters || {}}
                onChange={(changes) => {
                  updateNode(selectedNode.id, { parameters: changes });
                }}
              />
            </TabsContent>

            <TabsContent value="settings" className="m-0 p-6">
              <FormBuilder
                fields={schema.settings}
                data={selectedNode.settings || {}}
                onChange={(changes) => {
                  updateNode(selectedNode.id, { settings: changes });
                }}
              />
            </TabsContent>
          </Tabs>
        </div>

        <div className="w-7/12">
          <TestEventPanel
            nodeId={selectedNode.id}
            nodeType={selectedNode.type}
            testInstructions="Test this node with sample data to ensure it's configured correctly."
          />
        </div>
      </div>
    </>
  );
};

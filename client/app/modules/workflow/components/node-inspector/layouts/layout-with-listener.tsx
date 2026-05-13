import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { NodeInspectorHeader } from "../node-inspector-header";
import { useState } from "react";
import { NodeSchema } from "../../node-schemas/node-schema.type";
import { FormBuilder } from "../form-builder/form-builder";
import { useWorkflow } from "@blocks-workflow/hooks";
import { ListenEventPanel } from "../shared";
import { OutputPanel } from "../shared/output-panel";

type LayoutWithListenerProps = {
  schema: NodeSchema | null;
};

export const LayoutWithListener = ({ schema }: LayoutWithListenerProps) => {
  const { selectedNode, updateNode } = useWorkflow();
  const [activeTab, setActiveTab] = useState("parameters");
  if (!selectedNode || !schema) return null;

  return (
    <div className="flex h-full flex-col">
      <NodeInspectorHeader />
      <div className="mt-6 flex flex-1 gap-6">
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

        <div className="grid min-w-0 w-7/12 grid-rows-3 gap-4 overflow-hidden border p-3">
          <ListenEventPanel />
          <div className="row-span-2 min-w-0 w-full overflow-hidden">
            <OutputPanel />
          </div>
        </div>
        
      </div>
    </div>
  );
};

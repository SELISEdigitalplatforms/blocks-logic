import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { NodeInspectorHeader } from "../node-inspector-header";
import { useState } from "react";
import { NodeSchema } from "../../node-schemas/node-schema.type";
import { FormBuilder } from "../form-builder/form-builder";
import { useWorkflow } from "@blocks-workflow/hooks";
import { InputPanel } from "../shared/input-panel/input-panel";
import { OutputPanel } from "../shared/output-panel/output-panel";

type LayoutWithIOProps = {
  schema: NodeSchema | null;
};

export const LayoutWithIO = ({ schema }: LayoutWithIOProps) => {
  const { selectedNode, updateNode } = useWorkflow();
  const [activeTab, setActiveTab] = useState<"parameters" | "settings">("parameters");
  const [isInputCollapsed, setIsInputCollapsed] = useState(false);
  const [isOutputCollapsed, setIsOutputCollapsed] = useState(false);

  const toggleInput = () => {
    if (!isInputCollapsed && isOutputCollapsed) {
      setIsOutputCollapsed(false);
    }
    setIsInputCollapsed(!isInputCollapsed);
  };

  const toggleOutput = () => {
    if (!isOutputCollapsed && isInputCollapsed) {
      setIsInputCollapsed(false);
    }
    setIsOutputCollapsed(!isOutputCollapsed);
  };

  if (!selectedNode || !schema) return null;

  return (
      <div className="flex h-full flex-col">
        <NodeInspectorHeader />

        <div className="mt-6 flex flex-1 overflow-hidden">
        <div className="flex w-5/12 flex-col overflow-hidden">
          <Tabs
            value={activeTab}
            onValueChange={(v) => setActiveTab(v as "parameters" | "settings")}
            className="flex flex-1 flex-col overflow-hidden"
          >
            {/* Tabs Header */}
            <TabsList className="mb-4 w-fit shrink-0">
              <TabsTrigger value="parameters">Parameters</TabsTrigger>
              <TabsTrigger value="settings">Settings</TabsTrigger>
            </TabsList>

            {/* Parameters */}
            <TabsContent value="parameters" className="flex-1 overflow-y-auto p-1 pr-6">
              <FormBuilder
                fields={schema.parameters}
                data={selectedNode.parameters || {}}
                onChange={(changes) => {
                  updateNode(selectedNode.id, { parameters: changes });
                }}
              />
            </TabsContent>

            {/* Settings */}
            <TabsContent value="settings" className="flex-1 overflow-y-auto pr-2">
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

        <div className="flex w-7/12 flex-col gap-4 overflow-hidden rounded border p-3">
          <InputPanel isCollapsed={isInputCollapsed} onToggleCollapse={toggleInput} />
          <OutputPanel isCollapsed={isOutputCollapsed} onToggleCollapse={toggleOutput} />
        </div>
      </div>
    </div>
  );
};

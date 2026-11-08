import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { NodeInspectorHeader } from "../node-inspector-header";
import { useState } from "react";
import type { ComponentType } from "react";
import { NodeSchema } from "../../node-schemas/node-schema.type";
import { FormBuilder } from "../form-builder/form-builder";
import { useWorkflow } from "@blocks-workflow/hooks";
import { NodeGuidePanel, TestEventPanel } from "../shared";
import { useT } from "@seliseblocks/genesis-os";

type LayoutWithTestProps = {
  schema: NodeSchema | null;
  guide?: ComponentType | null;
};

export const LayoutWithTest = ({ schema, guide }: LayoutWithTestProps) => {
  const { t } = useT("logic.workflow.nodes.inspector");
  const { selectedNode, updateNode, updateNodeParameters } = useWorkflow();
  const [activeTab, setActiveTab] = useState("parameters");
  const [isGuideOpen, setIsGuideOpen] = useState(false);
  if (!selectedNode || !schema) return null;

  return (
    <div className="flex h-full flex-col">
      <NodeInspectorHeader
        hasGuide={Boolean(guide)}
        isGuideOpen={isGuideOpen}
        onToggleGuide={() => setIsGuideOpen((value) => !value)}
      />
      <div className="mt-6 flex flex-1 gap-6 overflow-hidden">
        <div className="flex w-5/12 flex-col overflow-hidden">
          <Tabs
            value={activeTab}
            onValueChange={setActiveTab}
            className="flex flex-1 flex-col overflow-hidden"
          >
            <TabsList className="mb-5 shrink-0">
              <TabsTrigger value="parameters">{t("LAYOUT_WITH_TEST.PARAMETERS")}</TabsTrigger>
              <TabsTrigger value="settings">{t("common:SETTINGS")}</TabsTrigger>
            </TabsList>

            <TabsContent value="parameters" className="flex-1 overflow-y-auto pr-6">
              <FormBuilder
                fields={schema.parameters}
                data={selectedNode.parameters || {}}
                onChange={(changes) => {
                  updateNodeParameters(selectedNode.id, changes);
                }}
              />
            </TabsContent>

            <TabsContent value="settings" className="m-0 flex-1 overflow-y-auto p-6">
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

        <div className="w-7/12 overflow-hidden">
          {guide && isGuideOpen ? (
            <NodeGuidePanel guide={guide} onClose={() => setIsGuideOpen(false)} />
          ) : (
            <TestEventPanel
              nodeId={selectedNode.id}
              nodeType={selectedNode.type}
              testInstructions={t("LAYOUT_WITH_TEST.TEST_THIS_NODE_WITH")}
            />
          )}
        </div>
      </div>
    </div>
  );
};

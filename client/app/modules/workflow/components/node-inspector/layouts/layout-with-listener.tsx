import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { NodeInspectorHeader } from "../node-inspector-header";
import { useState } from "react";
import type { ComponentType } from "react";
import { NodeSchema } from "../../node-schemas/node-schema.type";
import { FormBuilder } from "../form-builder/form-builder";
import { useWorkflow } from "@blocks-workflow/hooks";
import { ListenEventPanel, NodeGuidePanel } from "../shared";
import { OutputPanel } from "../shared/output-panel/output-panel";

type LayoutWithListenerProps = {
  schema: NodeSchema | null;
  guide?: ComponentType | null;
};

export const LayoutWithListener = ({ schema, guide }: LayoutWithListenerProps) => {
  const { selectedNode, updateNode, editorMode } = useWorkflow();
  const [activeTab, setActiveTab] = useState<"parameters" | "settings">("parameters");
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
        <div className="flex min-w-0 w-5/12 flex-col overflow-hidden">
          <Tabs
            value={activeTab}
            onValueChange={(value) => setActiveTab(value as "parameters" | "settings")}
            className="flex flex-1 flex-col overflow-hidden"
          >
            <TabsList className="mb-4 w-fit shrink-0">
              <TabsTrigger value="parameters">Parameters</TabsTrigger>
              <TabsTrigger value="settings">Settings</TabsTrigger>
            </TabsList>

            <TabsContent value="parameters" className="flex-1 overflow-y-auto p-1 pr-6">
              <FormBuilder
                fields={schema.parameters}
                data={selectedNode.parameters || {}}
                onChange={(changes) => {
                  updateNode(selectedNode.id, { parameters: changes });
                }}
              />
            </TabsContent>

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

        <div className="min-w-0 w-7/12 overflow-hidden">
          {guide && isGuideOpen ? (
            <NodeGuidePanel guide={guide} onClose={() => setIsGuideOpen(false)} />
          ) : (
            <div className="grid h-full min-w-0 grid-rows-[minmax(0,1fr)_minmax(0,1fr)_minmax(0,1fr)] gap-4 overflow-hidden rounded border p-3">
              {editorMode === "editor" && <ListenEventPanel />}
              <div
                className={`min-w-0 w-full overflow-hidden ${editorMode === "editor" ? "row-span-2" : "row-span-3"}`}
              >
                <OutputPanel />
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

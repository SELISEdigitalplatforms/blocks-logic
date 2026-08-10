import { Node } from "@xyflow/react";

export type NodeType =
  | "if"
  | "setfield"
  | "code"
  | "webhookResponse"
  | "manual"
  | "event"
  | "webhook"
  | "agent"
  | "sendMail"
  | "httpRequest"
  | "email"
  | "dataGateway"
  | "dataAction"
  | "blockschedule";

export type NodeCategory = "trigger" | "action" | "logic" | "transform";
export type NodeVersion = "v1" | "v2" | "v3";

export interface WorkflowNode extends Node {
  id: string;
  name: string;
  description: string;
  type: NodeType;
  category: NodeCategory;
  version: NodeVersion;
  isComingSoon?: boolean;
  position: {
    x: number;
    y: number;
  };
  parameters?: Record<string, unknown>;
  settings?: Record<string, unknown>;
  //temp
  isComplete?: boolean;
  pinData?: unknown[];
}

export interface EditorNode extends Node, WorkflowNode {
  type: NodeType;
  isComplete?: boolean;
}

export interface WorkflowNodeDefinition {
  defaultName?: string;
  id: `${WorkflowNodeDefinition["category"]}-${WorkflowNodeDefinition["type"]}-${WorkflowNodeDefinition["version"]}`;
  type: NodeType;
  title: string;
  description: string;
  icon: React.ReactNode;
  category: NodeCategory;
  version: NodeVersion;
  isComingSoon?: boolean;
  handleSpec: {
    source: string[];
    target: string[];
  };
}

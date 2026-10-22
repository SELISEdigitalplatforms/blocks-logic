import { Node } from "@xyflow/react";

export type NodeType =
  | "if"
  | "switch"
  | "setfield"
  | "splitOut"
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
  | "function"
  | "proxy"
  | "schedule";

export type NodeCategory = "trigger" | "action" | "logic" | "transform";
export type NodeVersion = "v1" | "v2" | "v3";

/** One connection point of a node. `key` is the xyflow handle id that edges reference. */
export type HandleEntry = { key: string; label: string };

/** The node's own connection points, stored with the node. */
export type NodeHandle = { sources: HandleEntry[]; targets: HandleEntry[] };

export interface WorkflowNode extends Node {
  id: string;
  name: string;
  description?: string;
  type: NodeType;
  category: NodeCategory;
  version: NodeVersion;
  isComingSoon?: boolean;
  handle?: NodeHandle | null;
  position: {
    x: number;
    y: number;
  };
  parameters?: Record<string, unknown>;
  settings?: Record<string, unknown>;
  //temp
  isComplete?: boolean;
  pinData?: unknown[] | null;
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
  /** The version new nodes of this type are created at; stored nodes at this or a lower version use this definition. */
  version: NodeVersion;
  isComingSoon?: boolean;
  /** Handle template copied into every new node; each `key` is the xyflow handle id that edges reference. */
  handle: NodeHandle;
  /** For types whose handles follow their parameters (Switch, Merge): rebuilds the handle on every load and edit. */
  deriveHandle?: (parameters: Record<string, unknown> | undefined) => NodeHandle;
  /** What a source handle and its edges show, when it is not simply the entry's label (Switch: "Output <i>"). */
  handleLabel?: (handle: NodeHandle | null | undefined, key: string | null | undefined) => string;
}

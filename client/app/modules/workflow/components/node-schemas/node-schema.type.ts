import {
  EditorNode,
  NodeCategory,
  NodeType,
  NodeVersion,
} from "@blocks-workflow/models/node.model";
import { FormField } from "../node-inspector/form-builder/form-field.types";

export interface NodeSchema {
  type: NodeType;
  category: NodeCategory;
  version: NodeVersion;
  parameters: FormField[];
  settings: FormField[];
}

export interface NodeSchemaDefinition {
  schema: NodeSchema;
  defaults: {
    parameters: Record<string, unknown>;
    settings: Record<string, unknown>;
  };
  transform?: (node: EditorNode) => EditorNode;
}

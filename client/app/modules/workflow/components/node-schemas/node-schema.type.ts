import {
  EditorNode,
  NodeCategory,
  NodeType,
  NodeVersion,
} from "@blocks-workflow/models/node.model";
import type { ComponentType } from "react";
import { FormField } from "../node-inspector/form-builder/form-field.types";
import type { FieldReferenceTarget } from "../node-inspector/shared/input-panel/utils/field-reference.util";
export interface NodeSchema {
  type: NodeType;
  category: NodeCategory;
  version: NodeVersion;
  parameters: FormField[];
  settings: FormField[];
}

export interface NodeSchemaDefinition {
  schema: NodeSchema;
  guide?: ComponentType;
  defaults: {
    parameters: Record<string, unknown>;
    settings: Record<string, unknown>;
  };
  transform?: (node: EditorNode) => EditorNode;
  /** How input-panel drags render for this node. Defaults to `{{…}}` expressions. */
  fieldReference?: (parameters: Record<string, unknown>) => FieldReferenceTarget;
}

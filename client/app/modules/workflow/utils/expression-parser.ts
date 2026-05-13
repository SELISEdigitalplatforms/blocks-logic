import { EditorNode } from "@blocks-workflow/models/node.model";

/**
 * Get handle ID for a node based on its category
 * Different node categories use different handles
 */
const getNodeHandleId = (node: EditorNode): string => {
  // Map node categories to their handle types
  const handleMap: Record<string, string> = {
    trigger: "trigger",
    action: "action",
    logic: "logic",
    transform: "transform",
  };

  return handleMap[node.category] || "main";
};

/**
 * Parse expression and convert node names (aliases) to backend format with node_id and handle_id
 *
 * Frontend expression (user sees): {{nodes.webhook.name}}
 * Backend format (stored): node_{webhook_id}_{handle_id}.name
 *
 * @param expression - The expression string with {{nodes.nodeName.field}} syntax
 * @param nodesMap - Map of all nodes (to find node by name/alias and get its ID)
 * @returns Expression with node IDs and handle IDs instead of names
 *
 * @example
 * Input:  "{{nodes.webhook.data.name}}"
 * Output: "node_abc123_trigger.data.name"
 */
export const parseExpressionToBackend = (
  expression: string,
  nodesMap: Record<string, EditorNode>,
): string => {
  if (!expression) return expression;

  // Create a map of node names (aliases) to node objects
  const nodeNameToNode: Record<string, EditorNode> = {};
  Object.values(nodesMap).forEach((node) => {
    const nodeName = node.name || node.type;
    nodeNameToNode[nodeName] = node;
  });

  // Replace all {{nodes.nodeName.field}} with node_{id}_{handle_id}.field
  return expression.replace(/\{\{nodes\.([\w ]+)(\.[\w.]+)?\}\}/g, (match, nodeName, fieldPath) => {
    const node = nodeNameToNode[nodeName];

    if (!node) return match;

    const nodeId = node.id;
    const handleId = getNodeHandleId(node);

    // Backend format: node_{id}_{handle_id}
    const backendNodeRef = `node_${nodeId}_${handleId}`;

    // If there's a field path, append it
    if (fieldPath) {
      return `${backendNodeRef}${fieldPath}`;
    }

    return backendNodeRef;
  });
};

/**
 * Convert backend format back to frontend expression (user-friendly alias)
 *
 * Backend format (stored): node_{webhook_id}_{handle_id}.name
 * Frontend expression (user sees): {{nodes.webhook.name}}
 *
 * @param backendExpression - The expression with node IDs and handle IDs
 * @param nodesMap - Map of all nodes (to find node by ID and get its name/alias)
 * @returns Expression with node names (aliases) instead of IDs
 *
 * @example
 * Input:  "node_abc123_trigger.data.name"
 * Output: "{{nodes.webhook.data.name}}"
 */
export const parseExpressionFromBackend = (
  backendExpression: string,
  nodesMap: Record<string, EditorNode>,
): string => {
  if (!backendExpression) return backendExpression;

  // Replace all node_{id}_{handle_id}.field with {{nodes.nodeName.field}}
  return backendExpression.replace(
    /node_([a-zA-Z0-9_-]+)_([a-zA-Z0-9_-]+)(\.[\w.]+)?/g,
    (match, nodeId, _handleId, fieldPath) => {
      const node = nodesMap[nodeId];

      if (!node) return match;

      // Use node name (alias) that user sees
      const nodeName = node.name || node.type;

      if (fieldPath) {
        return `{{nodes.${nodeName}${fieldPath}}}`;
      }

      return `{{nodes.${nodeName}}}`;
    },
  );
};

/**
 * Extract all node references from an expression
 *
 * @param expression - The expression string
 * @returns Array of node names referenced in the expression
 */
export const extractNodeReferences = (expression: string): string[] => {
  if (!expression) return [];

  const nodeRefs: string[] = [];
  const regex = /\{\{nodes\.([\w ]+)/g;
  let match;

  while ((match = regex.exec(expression)) !== null) {
    if (!nodeRefs.includes(match[1])) {
      nodeRefs.push(match[1]);
    }
  }

  return nodeRefs;
};

/**
 * Validate that all node references in an expression exist in the workflow
 *
 * @param expression - The expression string
 * @param nodesMap - Map of all nodes
 * @returns Object with validation result and errors
 */
export const validateExpression = (
  expression: string,
  nodesMap: Record<string, EditorNode>,
): { isValid: boolean; errors: string[] } => {
  if (!expression) return { isValid: true, errors: [] };

  const nodeRefs = extractNodeReferences(expression);
  const errors: string[] = [];

  // Create a map of node names
  const nodeNames = new Set(Object.values(nodesMap).map((node) => node.name || node.type));

  nodeRefs.forEach((nodeName) => {
    if (!nodeNames.has(nodeName)) {
      errors.push(`Node "${nodeName}" does not exist in the workflow`);
    }
  });

  return {
    isValid: errors.length === 0,
    errors,
  };
};

/**
 * Get the backend reference for a node by its name/alias
 * Useful when you need to manually construct backend references
 *
 * @param nodeName - The name/alias of the node (e.g., "webhook")
 * @param nodesMap - Map of all nodes
 * @returns Backend reference string or null if node not found
 *
 * @example
 * getNodeBackendRef("webhook", nodesMap)
 * Returns: "node_abc123_trigger"
 */
export const getNodeBackendRef = (
  nodeName: string,
  nodesMap: Record<string, EditorNode>,
): string | null => {
  const node = Object.values(nodesMap).find((n) => (n.name || n.type) === nodeName);

  if (!node) {
    return null;
  }

  const handleId = getNodeHandleId(node);
  return `node_${node.id}_${handleId}`;
};

/**
 * Get the frontend alias for a node by its ID
 * Useful when you need to show user-friendly names
 *
 * @param nodeId - The ID of the node
 * @param nodesMap - Map of all nodes
 * @returns Node name/alias or null if node not found
 *
 * @example
 * getNodeAlias("abc123", nodesMap)
 * Returns: "webhook"
 */
export const getNodeAlias = (
  nodeId: string,
  nodesMap: Record<string, EditorNode>,
): string | null => {
  const node = nodesMap[nodeId];
  return node ? node.name || node.type : null;
};

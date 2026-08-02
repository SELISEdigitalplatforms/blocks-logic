"use client";
import { Input } from "@/components/ui-kits/input/input";
import { FieldProps } from "../form-field.types";
import { useEffect, useState, useRef, useMemo } from "react";
import { useWorkflowStore } from "@blocks-workflow/store";
import { Edge } from "@xyflow/react";
import { EditorNode } from "@blocks-workflow/models/node.model";
import { cn } from "@/lib/utils";
import { ExpressionHighlighter } from "../utils/expression-highlighter";

interface AncestorNode {
  id: string;
  name: string;
  type: string;
  handle: string;
}

interface SuggestionItem {
  type: "node" | "handle" | "property";
  label: string; // Display text (e.g., "nodes.webhook_main")
  value: string; // Stored value (e.g., "node_123_main")
  description?: string;
}

/**
 * Recursively finds all ancestor nodes (parents + grandparents) for a given node
 * Returns nodes with their connection handles from edges
 *
 * @param nodeId - Current node ID to find ancestors for
 * @param nodesMap - Map of all nodes in the workflow
 * @param edgesMap - Map of all edges in the workflow
 * @returns Array of ancestor nodes with their handles
 */
const getAncestorNodes = (
  nodeId: string,
  nodesMap: Record<string, EditorNode>,
  edgesMap: Record<string, Edge>,
): AncestorNode[] => {
  const ancestors: AncestorNode[] = [];
  const visited = new Set<string>();
  const queue: string[] = [nodeId];

  while (queue.length > 0) {
    const currentNodeId = queue.shift()!;

    if (visited.has(currentNodeId)) {
      continue;
    }
    visited.add(currentNodeId);

    // Find all incoming edges (parent connections)
    const parentEdges = Object.values(edgesMap).filter((edge) => edge.target === currentNodeId);

    for (const edge of parentEdges) {
      const parentId = edge.source;
      const parentNode = nodesMap[parentId];

      if (parentNode && parentId !== nodeId) {
        // Avoid duplicate entries for the same node+handle combination
        if (!ancestors.find((a) => a.id === parentId && a.handle === (edge.sourceHandle || ""))) {
          ancestors.push({
            id: parentId,
            name: parentNode.name || parentNode.type,
            type: parentNode.type,
            handle: edge.sourceHandle || "",
          });
        }
        queue.push(parentId);
      }
    }
  }

  return ancestors;
};

export const ExpressionInputField = ({
  field,
  value,
  onChange,
  readOnly,
  config,
  className,
  placeholder="",
}: FieldProps<string>) => {
  const [showSuggestions, setShowSuggestions] = useState(false);
  const [cursorPosition, setCursorPosition] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const suggestionsRef = useRef<HTMLDivElement>(null);

  const { nodesMap, edgesMap } = useWorkflowStore();

  const ancestorNodes = useMemo(() => {
    if (!config?.nodeId) return [];
    return getAncestorNodes(config.nodeId, nodesMap, edgesMap);
  }, [config?.nodeId, nodesMap, edgesMap]);

  const parseExpression = (text: string, position: number) => {
    const beforeCursor = text.substring(0, position);

    // Match: {{ nodes.webhook_{handle}.keys.xxx
    const handleKeysMatch = beforeCursor.match(/\{\{nodes\.webhook_(\w+)\.keys\.?(\w*)$/);
    if (handleKeysMatch) {
      return {
        level: "keys",
        handleName: handleKeysMatch[1],
        partial: handleKeysMatch[2] || "",
      };
    }

    // Match: {{ nodes.webhook_{handle}.xxx → suggest "keys"
    const webhookMatch = beforeCursor.match(/\{\{nodes\.webhook_(\w+)\.?(\w*)$/);
    if (webhookMatch) {
      return {
        level: "webhook_property",
        handleName: webhookMatch[1],
        partial: webhookMatch[2] || "",
      };
    }

    // Match: {{ nodes.xxx → suggest node aliases
    const nodesMatch = beforeCursor.match(/\{\{nodes\.(\w*)$/);
    if (nodesMatch) {
      return {
        level: "node",
        partial: nodesMatch[1] || "",
      };
    }

    return null;
  };

  /**
   * Checks if the current cursor position triggers autocomplete
   */
  const checkForExpressionTrigger = (text: string, position: number) => {
    const parseResult = parseExpression(text, position);
    setShowSuggestions(!!parseResult);
    return parseResult;
  };

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const newValue = e.target.value;
    const position = e.target.selectionStart || 0;

    onChange(newValue);
    setCursorPosition(position);
    checkForExpressionTrigger(newValue, position);
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    // Allow closing suggestions with Escape
    if (e.key === "Escape" && showSuggestions) {
      setShowSuggestions(false);
      e.preventDefault();
    }
  };

  /**
   * Inserts a selected suggestion into the input field
   *
   * CRITICAL: This performs the mapping transformation
   * - User sees: {{ nodes.webhook_main.keys }}
   * - We save: {{ node_123_main.keys }}
   *
   * @param suggestion - The selected suggestion item
   */
  const insertNodeExpression = (suggestion: SuggestionItem) => {
    const text = value || "";
    const beforeCursor = text.substring(0, cursorPosition);
    const afterCursor = text.substring(cursorPosition);

    // Find the start of the expression
    const expressionStart = beforeCursor.lastIndexOf("{{nodes.");
    if (expressionStart === -1) return;

    const newValue = text.substring(0, expressionStart) + `{{${suggestion.value}}}` + afterCursor;

    onChange(newValue);
    setShowSuggestions(false);

    // Set cursor position after insertion
    setTimeout(() => {
      if (inputRef.current) {
        const newCursorPos = expressionStart + `{{${suggestion.value}}}`.length;
        inputRef.current.setSelectionRange(newCursorPos, newCursorPos);
        inputRef.current.focus();
      }
    }, 0);
  };

  const getFilteredSuggestions = (): SuggestionItem[] => {
    const text = value || "";
    const parseResult = parseExpression(text, cursorPosition);

    if (!parseResult) return [];

    const { level, partial, handleName } = parseResult;

    switch (level) {
      case "node":
        // Show available ancestor nodes in format: webhook_{handle}
        // Filter by partial input (what user has typed after "nodes.")
        return ancestorNodes
          .filter((node) => {
            if (!node.handle) return false;
            const displayName = `webhook_${node.handle}`;
            return displayName.toLowerCase().includes(partial.toLowerCase());
          })
          .map((node) => ({
            type: "handle",
            label: `nodes.${node.name}.${node.handle}`, // What user sees
            value: `node_${node.id}_${node.handle}`, // What we store
            description: `${node.name} (${node.type})`,
          }));

      case "webhook_property":
        // Show properties available on webhook handles (e.g., "keys")
        if ("keys".includes(partial.toLowerCase())) {
          const node = ancestorNodes.find((n) => n.handle === handleName);
          if (!node) return [];

          return [
            {
              type: "property",
              label: `nodes.${handleName}.keys`, // Display
              value: `node_${node.id}_${handleName}.keys`, // Storage
              description: "Webhook keys",
            },
          ];
        }
        return [];

      case "keys":
        // Future: Show specific key names if available from schema/metadata
        // For now, return empty as we don't have key data in frontend
        return [];

      default:
        return [];
    }
  };

  // Close suggestions when clicking outside
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (
        suggestionsRef.current &&
        !suggestionsRef.current.contains(e.target as Node) &&
        inputRef.current &&
        !inputRef.current.contains(e.target as Node)
      ) {
        setShowSuggestions(false);
      }
    };

    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const filteredSuggestions = showSuggestions ? getFilteredSuggestions() : [];

  const isDisabled = typeof field.disabled === "function" ? false : field.disabled || readOnly;

  return (
    <div className={cn("relative flex-1")}>
      <ExpressionHighlighter value={(value as string) || ""} isMultiline={false}>
        <Input
          ref={inputRef}
          id={field.id}
          type="text"
          value={(value as string) || ""}
          onChange={handleInputChange}
          onKeyDown={handleKeyDown}
          placeholder={placeholder || field.placeholder}
          disabled={isDisabled}
          className={cn("", className)}
        />
      </ExpressionHighlighter>

      {showSuggestions && filteredSuggestions.length > 0 && (
        <div
          ref={suggestionsRef}
          className="absolute z-50 mt-1 w-full rounded-md border border-border bg-popover shadow-lg"
        >
          <div className="max-h-60 overflow-y-auto p-1">
            {filteredSuggestions.map((suggestion, index) => (
              <button
                key={`${suggestion.value}-${index}`}
                onClick={() => insertNodeExpression(suggestion)}
                className="flex w-full items-center gap-2 rounded-sm px-3 py-2 text-left text-sm hover:bg-accent hover:text-accent-foreground"
              >
                <span className="font-medium">{suggestion.label}</span>
                {suggestion.description && (
                  <span className="text-xs text-muted-foreground">({suggestion.description})</span>
                )}
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
};

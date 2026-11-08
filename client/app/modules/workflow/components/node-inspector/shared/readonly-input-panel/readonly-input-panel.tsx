import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { ScrollArea, ScrollBar } from "@/components/ui-kits/scroll-area/scroll-area";
import { useWorkflowStore } from "@blocks-workflow/store";
import { useMemo, useState } from "react";
import { inferSchemaFromRuntimeRows } from "@blocks-workflow/utils/runtime-node-data";
import { ChevronDown, ChevronUp } from "lucide-react";
import type { ExecutedNode } from "@blocks-workflow/models/workflow.model";
import { CopyValueButton } from "../copy-value-button";
import { useT, tStatic } from "@seliseblocks/genesis-os";

const LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE = "logic.workflow.nodes.inspector";

/**
 * The Input panel of the execution and version views (every editorMode except "editor", which keeps the
 * draggable InputPanel). It shows the selected node's own recorded input from its latest run, laid out
 * like OutputPanel: the same header, Schema/Table/JSON tabs, item cards and copy buttons, but read-only,
 * with no mock data, alerts, run summaries or branch headers.
 *
 * The rendering is copied from OutputPanel on purpose, not shared (spec decision A3), so a style change
 * to either panel has to be made in both files.
 */
export const ReadonlyInputPanel = ({
  isCollapsed,
  onToggleCollapse,
}: {
  isCollapsed?: boolean;
  onToggleCollapse?: () => void;
} = {}) => {
  const { t } = useT(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE);
  const [tab, setTab] = useState("schema");
  const selectedNode = useWorkflowStore((s) => s.selectedNode);
  const executedNodes = useWorkflowStore((s) => s.executedNodes);

  // Only these two inputs: other store changes (nodesMap edits, items) do not recompute the rows or schema.
  const { rows, rowKeys, schema } = useMemo(() => {
    const run = selectedNode ? latestRun(executedNodes, selectedNode.id) : null;
    const input: unknown[] = Array.isArray(run?.input) ? run.input : [];
    return { rows: input, rowKeys: positionKeys(input), schema: inferSchemaFromRuntimeRows(input) };
  }, [executedNodes, selectedNode]);

  if (!selectedNode) return null;

  return (
    <div className={`flex w-full flex-col overflow-hidden ${isCollapsed ? 'h-fit shrink-0' : 'h-full flex-1'}`}>
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <h3 className="font-medium text-high-emphasis">{t("INPUT_PANEL.INPUT")}</h3>
        </div>
        <div className="flex items-center gap-2">
          <Tabs value={tab} onValueChange={setTab}>
            <TabsList>
              <TabsTrigger value="schema">{t("INPUT_PANEL.SCHEMA")}</TabsTrigger>
              <TabsTrigger value="table">{t("INPUT_PANEL.TABLE")}</TabsTrigger>
              <TabsTrigger value="json">JSON</TabsTrigger>
            </TabsList>
          </Tabs>
          {onToggleCollapse && (
            <button
              onClick={onToggleCollapse}
              aria-label={isCollapsed ? t("INPUT_PANEL.EXPAND_INPUT") : t("INPUT_PANEL.COLLAPSE_INPUT")}
              className="text-low-emphasis hover:bg-surface-hover rounded p-1 transition-colors"
            >
              {isCollapsed ? <ChevronDown size={18} /> : <ChevronUp size={18} />}
            </button>
          )}
        </div>
      </div>

      {!isCollapsed && tab === "schema" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          {rows.length === 0 ? (
            <p className="text-xs text-low-emphasis">{t("SCHEMA_TAB.NO_RUNTIME_INPUT_SCHEMA")}</p>
          ) : (
            <div className="flex flex-col gap-3">
              <div className="flex flex-col gap-2">
                {rows.map((row, index) => (
                  <div key={rowKeys[index]} className="rounded border border-border/60 p-2">
                    <p className="mb-1 text-xs font-semibold text-medium-emphasis">
                      {t("SCHEMA_TAB.ITEM", { value: index + 1 })}
                    </p>
                    {isRecord(row) && schema.length > 0 ? (
                      <div className="flex flex-col gap-1">
                        {schema.map((field) => (
                          <div
                            key={`${rowKeys[index]}-${field.key}`}
                            className="hover:bg-surface-hover group flex min-w-0 items-start gap-2 rounded px-2 py-1 text-xs"
                          >
                            <span className="shrink-0 py-0.5 font-mono text-high-emphasis">{field.key}:</span>
                            <span className="min-w-0 flex-1 whitespace-pre-wrap py-0.5 text-low-emphasis [overflow-wrap:anywhere]">
                              {formatCellValue(row[field.key])}
                            </span>
                            <CopyValueButton
                              value={toCopyText(row[field.key])}
                              label={t("OUTPUT_PANEL.COPY_VALUE_OF", { key: field.key })}
                              className="-my-0.5 h-5 px-1 opacity-0 focus-visible:opacity-100 group-hover:opacity-100"
                              iconClassName="h-3.5 w-3.5"
                            />
                          </div>
                        ))}
                      </div>
                    ) : (
                      <p className="whitespace-pre-wrap px-2 py-1 text-xs text-low-emphasis [overflow-wrap:anywhere]">
                        {formatCellValue(row)}
                      </p>
                    )}
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {!isCollapsed && tab === "table" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          <RuntimeInputTable rows={rows} rowKeys={rowKeys} />
        </div>
      )}

      {!isCollapsed && tab === "json" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app">
          <RuntimeInputJson rows={rows} />
        </div>
      )}
    </div>
  );
};

function RuntimeInputTable({ rows, rowKeys }: Readonly<{ rows: unknown[]; rowKeys: string[] }>) {
  const { t } = useT(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE);
  if (rows.length === 0) {
    return <p className="text-xs text-low-emphasis">{t("TABLE_TAB.NO_RUNTIME_INPUT_DATA")}</p>;
  }

  const primitiveColumn = t("JSON_TAB.VALUE");
  const hasPrimitiveRows = rows.some((row) => !isRecord(row));
  const objectColumns = Array.from(
    rows.reduce<Set<string>>((acc, row) => {
      if (!isRecord(row)) return acc;
      Object.keys(row).forEach((key) => acc.add(key));
      return acc;
    }, new Set<string>()),
  );

  const columns: string[] = hasPrimitiveRows ? [primitiveColumn, ...objectColumns] : objectColumns;

  return (
    <ScrollArea className="h-full w-full whitespace-nowrap rounded">
      <table className="min-w-max border-separate border-spacing-0 text-xs">
        <thead>
          <tr>
            {columns.map((column) => (
              <th
                key={column}
                className="border-b border-border px-2 py-1 text-left font-semibold text-medium-emphasis"
              >
                {column}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={rowKeys[index]}>
              {columns.map((column) => (
                <td
                  key={column}
                  className="max-w-xs whitespace-normal border-b border-border/50 px-2 py-1 align-top text-high-emphasis [overflow-wrap:anywhere]"
                >
                  {tableCellText(row, column === primitiveColumn ? null : column)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      <ScrollBar orientation="horizontal" />
    </ScrollArea>
  );
}

function RuntimeInputJson({ rows }: Readonly<{ rows: unknown[] }>) {
  const { t } = useT(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE);
  if (rows.length === 0) {
    return <p className="p-3 text-xs text-low-emphasis">{t("JSON_TAB.NO_RUNTIME_INPUT_DATA")}</p>;
  }

  const json = JSON.stringify(rows, null, 2);

  return (
    <div className="relative">
      <CopyValueButton value={json} label={t("JSON_TAB.COPY_INPUT_JSON")} className="absolute right-2 top-2" />
      <pre className="whitespace-pre-wrap p-3 pr-12 text-xs leading-relaxed text-high-emphasis [overflow-wrap:anywhere]">
        <code>{json}</code>
      </pre>
    </div>
  );
}

/**
 * A table cell: a primitive row's value in the "(value)" column (column null), an object row's field in
 * its own column, and empty text where the row has no such cell. Same output as OutputPanel's table.
 */
function tableCellText(row: unknown, column: string | null): string {
  if (column === null) return isRecord(row) ? "" : formatCellValue(row);
  return isRecord(row) ? formatCellValue(row[column]) : "";
}

/**
 * React keys for the recorded input rows. The rows are a fixed snapshot of one run that is never reordered,
 * edited or filtered, and carry no id of their own, so the position identifies a row.
 */
function positionKeys(rows: unknown[]): string[] {
  return rows.map((_, position) => `input-${position}`);
}

/** The node's run with the highest runIndex; on a tie the later entry wins (as in OutputPanel). */
function latestRun(executedNodes: ExecutedNode[] | null | undefined, nodeId: string): ExecutedNode | null {
  return (executedNodes ?? [])
    .filter((ne) => ne.nodeId === nodeId)
    .reduce<ExecutedNode | null>((last, ne) => (!last || ne.runIndex >= last.runIndex ? ne : last), null);
}

function formatCellValue(value: unknown): string {
  if (value === null || value === undefined) return String(value);
  if (typeof value === "string") return value;
  if (typeof value === "number" || typeof value === "boolean") return String(value);

  try {
    return JSON.stringify(value);
  } catch {
    return tStatic(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE, "OUTPUT_PANEL.UNSERIALIZABLE");
  }
}

function toCopyText(value: unknown): string {
  return typeof value === "string" ? value : formatCellValue(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === "object" && !Array.isArray(value);
}

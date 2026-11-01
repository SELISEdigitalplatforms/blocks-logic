import { CopyValueButton } from "../../copy-value-button";
import { DraggableArrayIndex, DraggableProperty } from "./draggable-property";
import { formatCellValue } from "../utils/format.util";
import {
  isPlainObject,
  type FieldReferenceTarget,
  type PathSegment,
} from "../utils/field-reference.util";
import { useT } from "@seliseblocks/genesis-os";

type RecursiveJsonViewerProps = Readonly<{
  data: unknown;
  depth?: number;
  segments?: PathSegment[];
  nodeName: string;
  isDirectParent: boolean;
  itemIndex: number;
  itemIsObject: boolean;
  target?: FieldReferenceTarget;
  isDraggable?: boolean;
}>;

type JsonTabProps = Readonly<{
  rows: unknown[];
  nodeName: string;
  isDirectParent: boolean;
  target?: FieldReferenceTarget;
  isDraggable?: boolean;
}>;

const getRowKey = (row: unknown) => {
  try {
    return typeof row === "object" && row !== null ? JSON.stringify(row) : String(row);
  } catch {
    return Object.prototype.toString.call(row);
  }
};

function RecursiveJsonViewer({
  data,
  depth = 0,
  segments = [],
  nodeName,
  isDirectParent,
  itemIndex,
  itemIsObject,
  target,
  isDraggable = true,
}: RecursiveJsonViewerProps) {
  const { t } = useT("logic.workflow.nodes.inspector");
  const referenceProps = { nodeName, isDirectParent, itemIndex, itemIsObject, target };

  if (typeof data !== "object" || data === null) {
    if (segments.length === 0) {
      return (
        <div className="flex min-w-0 items-start gap-2">
          <DraggableProperty
            segments={[]}
            {...referenceProps}
            label={t("JSON_TAB.VALUE")}
            isDraggable={isDraggable}
          />
          <span className="min-w-0 whitespace-pre-wrap [overflow-wrap:anywhere] py-1.5 text-green-600 dark:text-green-400">{formatCellValue(data)}</span>
        </div>
      );
    }
    return <span className="min-w-0 whitespace-pre-wrap [overflow-wrap:anywhere] py-1.5 text-green-600 dark:text-green-400">{formatCellValue(data)}</span>;
  }

  const entries = Object.entries(data);
  const isArray = Array.isArray(data);

  return (
    <div className="flex flex-col w-full font-mono text-xs">
      <span style={{ marginLeft: `${depth * 1}rem` }}>{isArray ? "[" : "{"}</span>
      {entries.map(([key, val], index) => {
        const currentSegments = [...segments, isArray ? Number(key) : key];
        const childIsObj = typeof val === "object" && val !== null;

        return (
          <div key={key} className="flex flex-col">
            <div className="flex min-w-0 items-start" style={{ marginLeft: `${(depth + 1) * 1}rem` }}>
              {isArray ? (
                <DraggableArrayIndex
                  segments={currentSegments}
                  label={`[${key}]`}
                  depth={0}
                  {...referenceProps}
                  isDraggable={isDraggable}
                  showColon={false}
                />
              ) : (
                <DraggableProperty
                  segments={currentSegments}
                  depth={0}
                  {...referenceProps}
                  label={`"${key}"`}
                  isDraggable={isDraggable}
                />
              )}
              {!childIsObj ? (
                <span className="min-w-0 whitespace-pre-wrap [overflow-wrap:anywhere] py-1.5 text-green-600 dark:text-green-400">
                  {formatCellValue(val)}
                  {index < entries.length - 1 ? "," : ""}
                </span>
              ) : null}
            </div>
            {childIsObj && (
              <div className="flex flex-col">
                <RecursiveJsonViewer
                  data={val}
                  depth={depth + 1}
                  segments={currentSegments}
                  {...referenceProps}
                  isDraggable={isDraggable}
                />
                <span style={{ marginLeft: `${(depth + 1) * 1}rem` }}>
                  {index < entries.length - 1 ? "," : ""}
                </span>
              </div>
            )}
          </div>
        );
      })}
      <span style={{ marginLeft: `${depth * 1}rem` }}>{isArray ? "]" : "}"}</span>
    </div>
  );
}

export function JsonTab({
  rows,
  nodeName,
  isDirectParent,
  target,
  isDraggable = true,
}: JsonTabProps) {
  const { t } = useT("logic.workflow.nodes.inspector");
  if (rows.length === 0) {
    return <p className="text-xs text-low-emphasis">{t("JSON_TAB.NO_RUNTIME_INPUT_DATA")}</p>;
  }

  const json = JSON.stringify(rows, null, 2);

  return (
    <div className="flex flex-col gap-3">
      <div className="flex justify-end">
        <CopyValueButton value={json} label={t("JSON_TAB.COPY_INPUT_JSON")} />
      </div>
      {rows.map((row, index) => (
        <div key={`json-row-${getRowKey(row)}`} className="rounded border border-border/60 p-2">
          <p className="mb-1 text-xs font-semibold text-medium-emphasis">{t("JSON_TAB.ITEM", { value: index + 1 })}</p>
          <RecursiveJsonViewer
            data={row}
            nodeName={nodeName}
            isDirectParent={isDirectParent}
            itemIndex={index}
            itemIsObject={isPlainObject(row)}
            target={target}
            isDraggable={isDraggable}
          />
        </div>
      ))}
    </div>
  );
}

import { ScrollArea, ScrollBar } from "@/components/ui-kits/scroll-area/scroll-area";
import { DraggableProperty } from "./draggable-property";
import { RecursiveSchemaViewer } from "./recursive-schema-viewer";
import { formatCellValue } from "../utils/format.util";
import { isPlainObject, type FieldReferenceTarget } from "../utils/field-reference.util";

type TableTabProps = Readonly<{
  rows: unknown[];
  nodeName: string;
  isDirectParent: boolean;
  target?: FieldReferenceTarget;
  isDraggable?: boolean;
}>;

type TableCellContext = Readonly<{
  column: string;
  row: unknown;
  rowIndex: number;
  primitiveColumn: string;
  nodeName: string;
  isDirectParent: boolean;
  target?: FieldReferenceTarget;
  isDraggable: boolean;
}>;

const getRowKey = (row: unknown) => {
  try {
    return typeof row === "object" && row !== null ? JSON.stringify(row) : String(row);
  } catch {
    return Object.prototype.toString.call(row);
  }
};

function renderTableCell({
  column,
  row,
  rowIndex,
  primitiveColumn,
  nodeName,
  isDirectParent,
  target,
  isDraggable,
}: TableCellContext) {
  const isRowObject = typeof row === "object" && row !== null;

  if (column === primitiveColumn) {
    return isRowObject ? "" : formatCellValue(row);
  }

  if (!isRowObject) {
    return "";
  }

  const value = (row as Record<string, unknown>)[column];
  if (typeof value !== "object" || value === null) {
    return formatCellValue(value);
  }

  return (
    <RecursiveSchemaViewer
      data={value}
      depth={0}
      segments={[column]}
      nodeName={nodeName}
      isDirectParent={isDirectParent}
      itemIndex={rowIndex}
      itemIsObject={isPlainObject(row)}
      target={target}
      isDraggable={isDraggable}
    />
  );
}

export function TableTab({
  rows,
  nodeName,
  isDirectParent,
  target,
  isDraggable = true,
}: TableTabProps) {
  if (rows.length === 0) {
    return <p className="text-xs text-low-emphasis">No runtime input data available.</p>;
  }

  const primitiveColumn = "(value)";
  const hasPrimitiveRows = rows.some((row) => typeof row !== "object" || row === null);
  const objectColumns = Array.from(
    rows.reduce<Set<string>>((acc, row) => {
      if (typeof row !== "object" || row === null) return acc;
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
                className="border-b border-border px-2 py-1 text-left font-semibold text-medium-emphasis align-top"
              >
                {column === primitiveColumn ? (
                  <DraggableProperty
                    segments={[]}
                    nodeName={nodeName}
                    isDirectParent={isDirectParent}
                    itemIndex={0}
                    itemIsObject={false}
                    target={target}
                    label={column}
                    isDraggable={isDraggable}
                    showColon={true}
                  />
                ) : (
                  <DraggableProperty
                    segments={[column]}
                    nodeName={nodeName}
                    isDirectParent={isDirectParent}
                    itemIndex={0}
                    itemIsObject={true}
                    target={target}
                    label={column}
                    isDraggable={isDraggable}
                    showColon={false}
                  />
                )}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => {
            return (
              <tr key={`table-row-${getRowKey(row)}`}>
                {columns.map((column) => (
                  <td
                    key={column}
                    className="border-b border-border/50 px-2 py-1 text-high-emphasis align-top"
                  >
                    {renderTableCell({
                      column,
                      row,
                      rowIndex: index,
                      primitiveColumn,
                      nodeName,
                      isDirectParent,
                      target,
                      isDraggable,
                    })}
                  </td>
                ))}
              </tr>
            );
          })}
        </tbody>
      </table>
      <ScrollBar orientation="horizontal" />
    </ScrollArea>
  );
}

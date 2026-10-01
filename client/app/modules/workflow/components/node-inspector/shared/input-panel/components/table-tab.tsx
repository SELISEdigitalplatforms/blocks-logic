import { ScrollArea, ScrollBar } from "@/components/ui-kits/scroll-area/scroll-area";
import { DraggableProperty } from "./draggable-property";
import { RecursiveSchemaViewer } from "./recursive-schema-viewer";
import { formatCellValue } from "../utils/format.util";
import { isPlainObject, type FieldReferenceTarget } from "../utils/field-reference.util";

export function TableTab({ rows, nodeName, isDirectParent, target, isDraggable = true }: { rows: unknown[]; nodeName: string; isDirectParent: boolean; target?: FieldReferenceTarget; isDraggable?: boolean }) {
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
                    isRoot={true}
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
            const isRowObj = typeof row === "object" && row !== null;
            return (
              <tr key={index}>
                {columns.map((column) => (
                  <td key={column} className="border-b border-border/50 px-2 py-1 text-high-emphasis align-top">
                    {column === primitiveColumn
                      ? !isRowObj
                        ? formatCellValue(row)
                        : ""
                      : isRowObj
                        ? (typeof (row as Record<string, unknown>)[column] === "object" && (row as Record<string, unknown>)[column] !== null) ? (
                            <RecursiveSchemaViewer 
                              data={(row as Record<string, unknown>)[column]} 
                              depth={0}
                              segments={[column]}
                              nodeName={nodeName}
                              isDirectParent={isDirectParent}
                              itemIndex={index}
                              itemIsObject={isPlainObject(row)}
                              target={target}
                              isDraggable={isDraggable}
                            />
                          ) : formatCellValue((row as Record<string, unknown>)[column])
                        : ""}
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

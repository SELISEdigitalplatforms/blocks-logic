import { ScrollArea, ScrollBar } from "@/components/ui-kits/scroll-area/scroll-area";
import { DraggableProperty } from "./draggable-property";
import { RecursiveSchemaViewer } from "./recursive-schema-viewer";
import { formatCellValue } from "../utils/format.util";

export function TableTab({ rows, nodeName, hasSinglePredecessor, isDraggable = true }: { rows: unknown[]; nodeName: string; hasSinglePredecessor: boolean; isDraggable?: boolean }) {
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
                    fieldKey=""
                    nodeName={nodeName}
                    hasSinglePredecessor={hasSinglePredecessor}
                    label={column}
                    isRoot={true}
                    isDraggable={isDraggable}
                  />
                ) : (
                  <DraggableProperty
                    fieldKey={column}
                    nodeName={nodeName}
                    hasSinglePredecessor={hasSinglePredecessor}
                    label={column}
                    isDraggable={isDraggable}
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
                        ? (typeof (row as any)[column] === "object" && (row as any)[column] !== null) ? (
                            <RecursiveSchemaViewer 
                              data={(row as any)[column]} 
                              depth={0}
                              prefixPath={column}
                              nodeName={nodeName}
                              hasSinglePredecessor={hasSinglePredecessor}
                              isDraggable={isDraggable}
                            />
                          ) : formatCellValue((row as any)[column])
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

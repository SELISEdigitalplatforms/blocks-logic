import { useState } from "react";
import { Loader2 } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { cn } from "@/lib/utils";
import { useGetVersionSource } from "../../hooks/use-versions";
import { IFunctionVersionSummary } from "../../types/version.types";
import { CodeEditor } from "../code-editor";

type VersionSourceDialogProps = {
  functionId: string;
  version: IFunctionVersionSummary | null;
  open: boolean;
  onOpenChange: (value: boolean) => void;
};

/** Read-only: a version is immutable, so this is a viewer and never an editor. */
export const VersionSourceDialog = ({
  functionId,
  version,
  open,
  onOpenChange,
}: VersionSourceDialogProps) => {
  const [activeFile, setActiveFile] = useState<"index.js" | "package.json">("index.js");
  const { data: source, isLoading } = useGetVersionSource(
    open ? functionId : undefined,
    open ? version?.id : undefined,
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-3xl">
        <DialogHeader>
          <DialogTitle>Source of v{version?.number}</DialogTitle>
          <DialogDescription>
            What this version was built from. It cannot be edited — roll back to make it active, or
            copy from here into the editor.
          </DialogDescription>
        </DialogHeader>

        <div className="flex border-b">
          {(["index.js", "package.json"] as const).map((file) => (
            <button
              key={file}
              type="button"
              className={cn(
                "border-b-2 px-4 py-2 font-mono text-xs",
                activeFile === file
                  ? "border-primary font-semibold text-primary"
                  : "border-transparent text-medium-emphasis hover:text-foreground",
              )}
              onClick={() => setActiveFile(file)}
            >
              {file}
            </button>
          ))}
        </div>

        {isLoading ? (
          <div className="flex h-[360px] items-center justify-center">
            <Loader2 className="h-5 w-5 animate-spin text-primary" />
          </div>
        ) : (
          <CodeEditor
            readOnly
            language={activeFile === "index.js" ? "javascript" : "json"}
            value={(activeFile === "index.js" ? source?.indexJs : source?.packageJson) ?? ""}
            height="360px"
          />
        )}
      </DialogContent>
    </Dialog>
  );
};

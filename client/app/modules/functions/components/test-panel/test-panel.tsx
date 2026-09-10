import { useState } from "react";
import { Play, Loader2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Label } from "@/components/ui-kits/label/label";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { CodeEditor } from "../code-editor";
import { RunStatusChip } from "../run-status-chip";
import { useTestFunction } from "../../hooks/use-runs";
import { useFunctionEditorStore } from "../../store/function-editor-store";
import { IInvokeResult } from "../../types/run.types";

type TestPanelProps = {
  functionId: string;
};

/** Runs `Save` implicitly server-side isn't assumed here — call Save before Test if there are unsaved changes. */
export const TestPanel = ({ functionId }: TestPanelProps) => {
  const testInput = useFunctionEditorStore((s) => s.testInput);
  const setTestInput = useFunctionEditorStore((s) => s.setTestInput);
  const [result, setResult] = useState<IInvokeResult | null>(null);
  const { mutateAsync, isPending } = useTestFunction();

  const handleRun = async () => {
    try {
      const response = await mutateAsync({ functionId, inputJson: testInput });
      setResult(response);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to run test" });
    }
  };

  return (
    <div className="space-y-4">
      <div className="space-y-1.5">
        <Label>Test input</Label>
        <CodeEditor language="json" value={testInput} onChange={setTestInput} height="160px" />
      </div>

      <Button type="button" onClick={handleRun} disabled={isPending} className="gap-1.5">
        {isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Play className="h-4 w-4" />}
        {isPending ? "Running…" : "Run test"}
      </Button>

      {result && (
        <div className="space-y-2 rounded-lg border p-3">
          <div className="flex items-center gap-2">
            <RunStatusChip status={result.status} />
            <span className="text-xs text-muted-foreground">Run {result.runId}</span>
          </div>
          {result.errorMessage && <p className="text-sm text-error">{result.errorMessage}</p>}
          {result.result && (
            <pre className="max-h-64 overflow-auto rounded-md bg-muted/30 p-2 font-mono text-xs">
              {result.result}
            </pre>
          )}
        </div>
      )}
    </div>
  );
};

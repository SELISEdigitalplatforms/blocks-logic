"use client";

import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Play, Loader2 } from "lucide-react";
import { Alert, AlertDescription } from "@/components/ui-kits/alert/alert";

interface TestEventPanelProps {
  nodeId: string;
  nodeType: string;
  onTest?: () => Promise<void>;
  testInstructions?: string;
}

export const TestEventPanel = ({ onTest, testInstructions }: TestEventPanelProps) => {
  const [isTesting, setIsTesting] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);

  const handleTest = async () => {
    setIsTesting(true);
    setTestResult(null);

    try {
      if (onTest) {
        await onTest();
        setTestResult({
          success: true,
          message: "Test completed successfully",
        });
      }
    } catch (error) {
      setTestResult({
        success: false,
        message: error instanceof Error ? error.message : "Test failed",
      });
    } finally {
      setIsTesting(false);
    }
  };

  return (
    <div className="space-y-4">
      <div className="rounded-lg border bg-muted/20 p-6">
        <h3 className="mb-2 text-sm font-medium">Test This Node</h3>
        <p className="mb-4 text-sm text-muted-foreground">
          {testInstructions ||
            "Execute this node with test data to verify it works as expected before running the full workflow."}
        </p>

        <Button onClick={handleTest} disabled={isTesting} className="gap-2">
          {isTesting ? (
            <>
              <Loader2 className="h-4 w-4 animate-spin" />
              Testing...
            </>
          ) : (
            <>
              <Play className="h-4 w-4" />
              Run Test
            </>
          )}
        </Button>
      </div>

      {testResult && (
        <Alert variant={testResult.success ? "default" : "destructive"}>
          <AlertDescription>{testResult.message}</AlertDescription>
        </Alert>
      )}
    </div>
  );
};

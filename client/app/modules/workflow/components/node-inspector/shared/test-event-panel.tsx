"use client";

import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Play, Loader2 } from "lucide-react";
import { Alert, AlertDescription } from "@/components/ui-kits/alert/alert";
import { useT } from "@seliseblocks/genesis-os";

interface TestEventPanelProps {
  nodeId: string;
  nodeType: string;
  onTest?: () => Promise<void>;
  testInstructions?: string;
}

export const TestEventPanel = ({ onTest, testInstructions }: TestEventPanelProps) => {
  const { t } = useT("logic.workflow.nodes.inspector");
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
          message: t("TEST_EVENT_PANEL.TEST_COMPLETED_SUCCESSFULLY"),
        });
      }
    } catch (error) {
      setTestResult({
        success: false,
        message: error instanceof Error ? error.message : t("TEST_EVENT_PANEL.TEST_FAILED"),
      });
    } finally {
      setIsTesting(false);
    }
  };

  return (
    <div className="space-y-4">
      <div className="rounded-lg border bg-muted/20 p-6">
        <h3 className="mb-2 text-sm font-medium">{t("TEST_EVENT_PANEL.TEST_THIS_NODE")}</h3>
        <p className="mb-4 text-sm text-muted-foreground">
          {testInstructions || t("TEST_EVENT_PANEL.EXECUTE_THIS_NODE_WITH")}
        </p>

        <Button onClick={handleTest} disabled={isTesting} className="gap-2">
          {isTesting ? (
            <>
              <Loader2 className="h-4 w-4 animate-spin" />
              {t("TEST_EVENT_PANEL.TESTING")}
            </>
          ) : (
            <>
              <Play className="h-4 w-4" />
              {t("TEST_EVENT_PANEL.RUN_TEST")}
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

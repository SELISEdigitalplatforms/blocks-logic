"use client";
import { type ComponentProps, useRef } from "react";
import { Upload } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { cn } from "@/lib/utils";
import { useImportWorkflow } from "@blocks-workflow/hooks/use-import-workflow";

type ImportWorkflowProps = {
  buttonClassName?: string;
  hideLabelOnMobile?: boolean;
  label?: string;
  showIcon?: boolean;
  variant?: ComponentProps<typeof Button>["variant"];
};

export const ImportWorkflow = ({
  buttonClassName,
  hideLabelOnMobile = true,
  label = "Import",
  showIcon = true,
  variant = "ghost",
}: ImportWorkflowProps) => {
  const inputRef = useRef<HTMLInputElement>(null);
  const { importWorkflow, isImporting } = useImportWorkflow();

  const handleChange = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    // Reset immediately so re-selecting the same file still fires `change`.
    event.target.value = "";
    if (!file) return;
    await importWorkflow(file);
  };

  return (
    <>
      <input
        ref={inputRef}
        type="file"
        accept="application/json,.json"
        className="hidden"
        aria-hidden="true"
        tabIndex={-1}
        disabled={isImporting}
        onChange={handleChange}
      />
      <Button
        type="button"
        size="sm"
        variant={variant}
        disabled={isImporting}
        onClick={() => inputRef.current?.click()}
        className={cn(
          variant === "ghost" && "text-primary hover:text-primary",
          buttonClassName,
        )}
      >
        {showIcon && <Upload className="h-4 w-4" />}
        <span className={cn(hideLabelOnMobile && "sr-only sm:not-sr-only", showIcon && "ml-2.5")}>
          {label}
        </span>
      </Button>
    </>
  );
};

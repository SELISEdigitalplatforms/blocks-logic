import { type DragEvent, type KeyboardEvent, type ReactNode, useState } from "react";
import { AlertTriangle, Check, ChevronDown, Copy, FileJson, Loader2, X } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { cn } from "@/lib/utils";
import { useT, tStatic } from "@seliseblocks/genesis-os";

/*
 * The small pieces both import dialogs are built from. Functions keeps its own identical copy
 * (`functions/components/function-import-dialog/import-parts.tsx`): Functions and Proxy do not
 * share code, so a change here is made there too. Nothing in this file knows what is imported.
 */

/** Where the user is: 0 choose a file, 1 review the preview, 2 imported. */
export const ImportSteps = ({ current }: { current: 0 | 1 | 2 }) => {
  const { t } = useT("logic.proxy.components");
  const steps = [
    t("PROXY_IMPORT_PARTS.CHOOSE_FILE"),
    t("PROXY_IMPORT_PARTS.REVIEW_CHANGES"),
    t("common:DONE"),
  ];
  return (
    <ol
      className="flex items-center gap-2 text-xs"
      aria-label={t("PROXY_IMPORT_PARTS.IMPORT_STEPS")}
    >
      {steps.map((label, index) => {
        const done = index < current;
        const active = index === current;
        return (
          <li
            key={label}
            className="flex items-center gap-2"
            aria-current={active ? "step" : undefined}
          >
            <span
              className={cn(
                "flex h-5 w-5 items-center justify-center rounded-full border text-[11px] font-semibold",
                done && "border-primary bg-primary text-primary-foreground",
                active && "border-primary text-primary",
                !done && !active && "border-border text-medium-emphasis",
              )}
            >
              {done ? <Check className="h-3 w-3" aria-hidden="true" /> : index + 1}
            </span>
            <span className={cn(active ? "font-semibold" : "text-medium-emphasis")}>{label}</span>
            {index < steps.length - 1 && <span className="h-px w-6 bg-border" aria-hidden="true" />}
          </li>
        );
      })}
    </ol>
  );
};

type DropZoneProps = {
  disabled?: boolean;
  /** Opens the file picker (click, Enter, Space). */
  onBrowse: () => void;
  /** The dropped files; the dialog validates them exactly like a picked one. */
  onDropFiles: (files: File[]) => void;
};

/** Drag a file here, or click / press Enter or Space to browse. */
export const ImportDropZone = ({ disabled, onBrowse, onDropFiles }: DropZoneProps) => {
  const { t } = useT("logic.proxy.components");
  const [dragging, setDragging] = useState(false);

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (disabled || (event.key !== "Enter" && event.key !== " ")) return;
    event.preventDefault();
    onBrowse();
  };

  const onDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setDragging(false);
    if (!disabled) onDropFiles([...event.dataTransfer.files]);
  };

  return (
    <div
      role="button"
      tabIndex={disabled ? -1 : 0}
      aria-disabled={disabled || undefined}
      aria-label={t("PROXY_IMPORT_PARTS.CHOOSE_A_EXPORT_FILE")}
      data-testid="import-drop-zone"
      data-dragging={dragging || undefined}
      className={cn(
        "flex flex-col items-center gap-2 rounded-lg border-2 border-dashed px-6 py-10 text-center transition-colors",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
        dragging ? "border-primary bg-primary/5" : "border-border hover:bg-muted/50",
        disabled ? "cursor-not-allowed opacity-60" : "cursor-pointer",
      )}
      onClick={() => !disabled && onBrowse()}
      onKeyDown={onKeyDown}
      onDragOver={(event) => {
        event.preventDefault();
        if (!disabled) setDragging(true);
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={onDrop}
    >
      <div className="flex h-12 w-12 items-center justify-center rounded-md bg-primary/10 text-primary">
        <FileJson className="h-6 w-6" aria-hidden="true" />
      </div>
      <p className="text-sm font-semibold">
        {dragging
          ? t("PROXY_IMPORT_PARTS.DROP_THE_FILE_TO")
          : t("PROXY_IMPORT_PARTS.DROP_A_EXPORT_HERE")}
      </p>
      <p className="text-xs text-medium-emphasis">{t("PROXY_IMPORT_PARTS.OR_CLICK_TO_BROWSE")}</p>
    </div>
  );
};

export const formatFileSize = (bytes: number) =>
  bytes < 1024
    ? `${bytes} B`
    : bytes < 1024 * 1024
      ? `${(bytes / 1024).toFixed(1)} KB`
      : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;

type FileCardProps = {
  name: string;
  size: number;
  /** True while the file is being read and checked. */
  checking?: boolean;
  disabled?: boolean;
  onReplace: () => void;
  onRemove: () => void;
};

/** The chosen file: name, size, and a way to swap or drop it. */
export const ImportFileCard = ({
  name,
  size,
  checking,
  disabled,
  onReplace,
  onRemove,
}: FileCardProps) => {
  const { t } = useT("logic.proxy.components");
  return (
    <div className="flex items-center gap-3 rounded-lg border p-3" data-testid="import-file-card">
      <FileJson className="h-5 w-5 shrink-0 text-primary" aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-semibold">{name}</p>
        <p className="text-xs text-medium-emphasis">
          {formatFileSize(size)}
          {checking ? t("PROXY_IMPORT_PARTS.CHECKING") : ""}
        </p>
      </div>
      {checking && (
        <Loader2 className="h-4 w-4 animate-spin text-medium-emphasis" aria-hidden="true" />
      )}
      <Button size="xs" variant="outline" disabled={disabled} onClick={onReplace}>
        {t("PROXY_IMPORT_PARTS.REPLACE")}
      </Button>
      <Button
        size="xs"
        variant="ghost"
        className="px-2"
        aria-label={t("PROXY_IMPORT_PARTS.REMOVE", { name })}
        disabled={disabled}
        onClick={onRemove}
      >
        <X className="h-4 w-4" />
      </Button>
    </div>
  );
};

const STATUS_CHIPS = {
  new: {
    get label() {
      return tStatic("logic.proxy.components", "PROXY_IMPORT_PARTS.NEW");
    },
    variant: "info",
  },
  same: {
    get label() {
      return tStatic("logic.proxy.components", "PROXY_IMPORT_PARTS.SAME");
    },
    variant: "secondary",
  },
  changed: {
    get label() {
      return tStatic("logic.proxy.components", "PROXY_IMPORT_PARTS.CHANGED");
    },
    variant: "warning",
  },
  blocked: {
    get label() {
      return tStatic("logic.proxy.components", "PROXY_IMPORT_PARTS.BLOCKED");
    },
    variant: "error",
  },
} as const;

export const ImportStatusChip = ({ status }: { status: keyof typeof STATUS_CHIPS }) => (
  <Badge
    variant={STATUS_CHIPS[status].variant}
    className="w-fit rounded-md px-2.5 py-0.5"
    data-testid="import-status-chip"
  >
    {STATUS_CHIPS[status].label}
  </Badge>
);

/** One "use mine / use incoming" switch. */
export const ImportChoiceToggle = ({
  label,
  value,
  onChange,
}: {
  label: string;
  value: "mine" | "incoming";
  onChange: (value: "mine" | "incoming") => void;
}) => {
  const { t } = useT("logic.proxy.components");
  return (
    <div
      role="group"
      aria-label={t("PROXY_IMPORT_PARTS.USE_FOR", { label })}
      className="inline-flex rounded-md border"
    >
      {(["mine", "incoming"] as const).map((choice) => (
        <button
          key={choice}
          type="button"
          aria-pressed={value === choice}
          className={cn(
            "px-2.5 py-1 text-xs font-medium first:rounded-l-md last:rounded-r-md",
            "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
            value === choice
              ? "bg-primary text-primary-foreground"
              : "text-medium-emphasis hover:bg-muted/50",
          )}
          onClick={() => onChange(choice)}
        >
          {choice === "mine" ? t("PROXY_IMPORT_PARTS.MINE") : t("PROXY_IMPORT_PARTS.INCOMING")}
        </button>
      ))}
    </div>
  );
};

type ChangeRowProps = {
  label: string;
  value: "mine" | "incoming";
  onChange: (value: "mine" | "incoming") => void;
  /** The expanded body: the two values, or an editor diff. */
  children: ReactNode;
};

/** One difference: its name, the Mine / Incoming switch, and an expandable detail. */
export const ImportChangeRow = ({ label, value, onChange, children }: ChangeRowProps) => {
  const { t } = useT("logic.proxy.components");
  const [open, setOpen] = useState(false);
  return (
    <div className="rounded-lg border" data-testid="import-change-row">
      <div className="flex flex-wrap items-center justify-between gap-2 p-3">
        <button
          type="button"
          aria-expanded={open}
          aria-label={t(
            open ? "PROXY_IMPORT_PARTS.HIDE_DIFF_FOR" : "PROXY_IMPORT_PARTS.SHOW_DIFF_FOR",
            { label },
          )}
          className="flex min-w-0 items-center gap-1.5 text-left text-sm font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          onClick={() => setOpen((current) => !current)}
        >
          <ChevronDown
            aria-hidden="true"
            className={cn("h-4 w-4 shrink-0 transition-transform", !open && "-rotate-90")}
          />
          <span className="truncate">{label}</span>
        </button>
        <ImportChoiceToggle label={label} value={value} onChange={onChange} />
      </div>
      {open && <div className="border-t p-3">{children}</div>}
    </div>
  );
};

/** The two sides of a short value, as plain text. */
export const ImportValuePair = ({
  mine,
  incoming,
}: {
  mine: string | null | undefined;
  incoming: string | null | undefined;
}) => {
  const { t } = useT("logic.proxy.components");
  return (
    <div className="grid gap-3 text-xs sm:grid-cols-2">
      {(
        [
          [t("PROXY_IMPORT_PARTS.MINE_HERE_NOW"), mine],
          [t("PROXY_IMPORT_PARTS.INCOMING_FILE"), incoming],
        ] as const
      ).map(([title, value]) => (
        <div key={title} className="min-w-0">
          <p className="mb-1 font-medium text-medium-emphasis">{title}</p>
          {value === null || value === undefined ? (
            <span className="italic text-muted-foreground">{t("PROXY_IMPORT_PARTS.NOT_SET")}</span>
          ) : (
            <pre className="max-h-40 overflow-auto whitespace-pre-wrap break-all font-mono">
              {value}
            </pre>
          )}
        </div>
      ))}
    </div>
  );
};

type SecretChecklistProps = {
  missing: string[];
  duplicate: string[];
  /** Referenced by the file but with no secret behind them where it was exported. */
  unresolved?: string[];
  /** The secret lookup was not available, so the lists above may be incomplete. */
  unchecked?: boolean;
};

/** Secrets the import needs, one line each, with what to do about it. */
export const ImportSecretChecklist = ({
  missing,
  duplicate,
  unresolved = [],
  unchecked,
}: SecretChecklistProps) => {
  const { t } = useT("logic.proxy.components");
  if (!missing.length && !duplicate.length && !unresolved.length && !unchecked) return null;
  const rows = [
    ...missing.map((name) => ({
      name,
      icon: AlertTriangle,
      text: t("PROXY_IMPORT_PARTS.NOT_IN_THIS_ENVIRONMENT"),
    })),
    ...duplicate.map((name) => ({
      name,
      icon: Copy,
      text: t("PROXY_IMPORT_PARTS.MORE_THAN_ONE_SECRET"),
    })),
    ...unresolved.map((name) => ({
      name,
      icon: AlertTriangle,
      text: t("PROXY_IMPORT_PARTS.HAD_NO_SECRET_WHERE"),
    })),
  ];
  return (
    <div
      className="rounded-md border border-warning-100 bg-warning-50 p-3 text-xs text-warning-800"
      data-testid="import-secret-checklist"
    >
      <p className="font-semibold">{t("PROXY_IMPORT_PARTS.SECRETS_TO_CHECK")}</p>
      {rows.length > 0 && (
        <ul className="mt-1.5 space-y-1">
          {rows.map(({ name, icon: Icon, text }) => (
            <li key={`${text}-${name}`} className="flex items-start gap-1.5">
              <Icon aria-hidden="true" className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              <span>
                <span className="font-mono font-semibold">{name}</span> — {text}
              </span>
            </li>
          ))}
        </ul>
      )}
      {unchecked && <p className="mt-1.5">{t("PROXY_IMPORT_PARTS.SECRETS_COULD_NOT_BE")}</p>}
    </div>
  );
};

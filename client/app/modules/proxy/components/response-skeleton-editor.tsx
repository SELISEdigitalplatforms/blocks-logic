import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { renderRichText } from "@/lib/rich-text";
import { useT } from "@seliseblocks/genesis-os";

type Props = {
  value: string;
  error: string | null;
  onChange: (value: string) => void;
};

/**
 * The JSON-skeleton pane. Editing it re-derives the Fields tree live, as long as the text parses
 * as JSON — only the key structure is read, every value is treated as `null`.
 */
export const ResponseSkeletonEditor = ({ value, error, onChange }: Props) => {
  const { t } = useT("logic.proxy.components");
  return (
    <div className="rounded-lg border bg-card p-3">
      <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
        {t("RESPONSE_SKELETON_EDITOR.JSON_SKELETON")}
      </p>
      <Textarea
        className="h-48 font-mono text-xs"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
      <p className="mt-2 text-[11px] text-muted-foreground">
        {renderRichText(t("RESPONSE_SKELETON_EDITOR.SHAPE_ONLY_VALUES_ARE"), [
          (children) => <code>{children}</code>,
        ])}
      </p>
      {error ? <p className="mt-1 text-xs text-destructive">{error}</p> : null}
    </div>
  );
};

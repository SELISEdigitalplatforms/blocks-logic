import { AlertTriangle } from "lucide-react";
import { KeyCollision, KeyKind } from "../utils";
import { useT } from "@seliseblocks/genesis-os";
import { renderRichText } from "@/lib/rich-text";

type Props = {
  /** The row's key as typed; shown trimmed. */
  rowKey: string;
  kind: KeyKind;
  collision: KeyCollision | undefined;
};

/**
 * Amber, non-blocking notes under one key/value row. These are not form errors — a same-name row is a
 * legitimate override — they only say what the gateway will actually send.
 */
export const KeyCollisionWarning = ({ rowKey, kind, collision }: Props) => {
  const { t } = useT("logic.proxy.components");
  if (!collision || (!collision.replacesConnection && !collision.duplicateInList)) return null;
  const key = rowKey.trim();
  const code = [(text: string) => <code className="font-mono">{text}</code>];

  return (
    <div className="mt-1 space-y-0.5">
      {collision.replacesConnection ? (
        <p className="flex items-center gap-1.5 text-[11px] text-amber-700 dark:text-amber-400">
          <AlertTriangle aria-hidden="true" className="h-3 w-3 shrink-0" />
          <span>
            {renderRichText(
              t(kind === "header" ? "KEY_COLLISION_WARNING.REPLACES_CONNECTION_HEADER" : "KEY_COLLISION_WARNING.REPLACES_CONNECTION_QUERY", { key }),
              code,
            )}
          </span>
        </p>
      ) : null}
      {collision.duplicateInList ? (
        <p className="flex items-center gap-1.5 text-[11px] text-amber-700 dark:text-amber-400">
          <AlertTriangle aria-hidden="true" className="h-3 w-3 shrink-0" />
          <span>
            {renderRichText(
              t(kind === "header" ? "KEY_COLLISION_WARNING.DUPLICATE_HEADER_SAVING_WILL" : "KEY_COLLISION_WARNING.DUPLICATE_ONLY_THE_LAST", { key }),
              code,
            )}
          </span>
        </p>
      ) : null}
    </div>
  );
};

import { AlertTriangle } from "lucide-react";
import { KeyCollision, KeyKind } from "../utils";

type Props = {
  /** The row's key as typed; shown trimmed. */
  rowKey: string;
  kind: KeyKind;
  collision: KeyCollision | undefined;
};

const noun = (kind: KeyKind) => (kind === "header" ? "header" : "query parameter");

/**
 * Amber, non-blocking notes under one key/value row. These are not form errors — a same-name row is a
 * legitimate override — they only say what the gateway will actually send.
 */
export const KeyCollisionWarning = ({ rowKey, kind, collision }: Props) => {
  if (!collision || (!collision.replacesConnection && !collision.duplicateInList)) return null;
  const key = <code className="font-mono">{rowKey.trim()}</code>;

  return (
    <div className="mt-1 space-y-0.5">
      {collision.replacesConnection ? (
        <p className="flex items-center gap-1.5 text-[11px] text-amber-700 dark:text-amber-400">
          <AlertTriangle aria-hidden="true" className="h-3 w-3 shrink-0" />
          <span>
            Replaces the connection&apos;s {key} {noun(kind)} on this endpoint.
          </span>
        </p>
      ) : null}
      {collision.duplicateInList ? (
        <p className="flex items-center gap-1.5 text-[11px] text-amber-700 dark:text-amber-400">
          <AlertTriangle aria-hidden="true" className="h-3 w-3 shrink-0" />
          <span>Duplicate — only the last {key} row is sent.</span>
        </p>
      ) : null}
    </div>
  );
};

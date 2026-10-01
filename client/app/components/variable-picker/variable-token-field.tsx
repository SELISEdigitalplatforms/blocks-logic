import { useRef } from "react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { SecretListItem } from "@/models/secret";
import { useVariableCatalog } from "@/hooks/use-variable-catalog";
import { VariablePickerPopover } from "./variable-picker-popover";
import {
  VariableRefCodec,
  displayToken,
  fromDisplayValue,
  hasRef,
  toDisplayValue,
  unresolvableRefKeys,
} from "./variable-ref";

export type VariableTokenFieldProps = {
  value: string;
  onChange: (value: string) => void;
  codec: VariableRefCodec;
  ariaLabel: string;
  /**
   * Supply the catalog instead of letting the field fetch it. For a form that already loads the
   * rows once and threads them down (Proxy), or a test that wants to control them.
   */
  variables?: SecretListItem[];
  isLoading?: boolean;
  isError?: boolean;
  placeholder?: string;
  /** Renders a textarea instead of a single-line input. */
  multiline?: boolean;
  rows?: number;
  className?: string;
};

/**
 * Free text that may carry configuration-variable references anywhere inside it — an
 * `Authorization` header, a body template — with a picker that inserts one at the caret.
 *
 * What is typed and what is stored differ on purpose. Under `secretIdRef` the stored token carries
 * an opaque id, which must never appear on screen; the field shows `{{variable.NAME}}` and
 * translates back to the id on every change. Under a name-keyed codec the translation is the
 * identity, which is what lets one component serve Proxy and Functions alike.
 *
 * When a reference cannot be named — the variable was deleted, or renamed out from under the
 * template — there is nothing safe to render in its place, so the field locks rather than exposing
 * the key or dropping it in a round trip. Clearing it is the offered way forward.
 */
export const VariableTokenField = ({
  value,
  onChange,
  codec,
  ariaLabel,
  variables,
  isLoading: isLoadingProp,
  isError: isErrorProp,
  placeholder,
  multiline,
  rows = 4,
  className,
}: VariableTokenFieldProps) => {
  // Skipped entirely when the caller supplies the rows, so a form that already has them does
  // not fetch the same list once per field.
  const query = useVariableCatalog({}, { enabled: !variables });
  const catalog = variables ?? query.data ?? [];
  const isLoading = isLoadingProp ?? (variables ? false : query.isLoading);
  const isError = isErrorProp ?? (variables ? false : query.isError);
  const caret = useRef<number | null>(null);

  // Nothing is unresolvable until the catalog has arrived, or every field would lock on mount.
  const locked = !isLoading && unresolvableRefKeys(value, codec, catalog).length > 0;
  const display = toDisplayValue(value, codec, catalog);

  const rememberCaret = (event: { currentTarget: { selectionStart: number | null } }) => {
    caret.current = event.currentTarget.selectionStart;
  };

  const handleChange = (next: string) => onChange(fromDisplayValue(next, codec, catalog));

  const insert = (token: string) => {
    const at = caret.current ?? display.length;
    const next = `${display.slice(0, at)}${token}${display.slice(at)}`;
    caret.current = at + token.length;
    handleChange(next);
  };

  const shared = {
    "aria-label": ariaLabel,
    placeholder,
    value: locked ? "" : display,
    readOnly: locked,
    onChange: (event: { target: { value: string } }) => handleChange(event.target.value),
    onSelect: rememberCaret,
    onKeyUp: rememberCaret,
    onClick: rememberCaret,
  };

  return (
    <div className="flex min-w-0 flex-col gap-1">
      <div className={multiline ? "flex flex-col gap-1.5" : "flex min-w-0 items-center gap-1.5"}>
        {locked ? (
          <div className="flex h-9 min-w-0 flex-1 items-center rounded-md border border-error/40 bg-error/5 px-2">
            <span className="truncate text-xs italic text-error">
              References a variable that no longer exists
            </span>
          </div>
        ) : multiline ? (
          <Textarea {...shared} rows={rows} className={className ?? "font-mono text-xs"} />
        ) : (
          <Input {...shared} className={className ?? "h-9 min-w-0 flex-1 font-mono text-xs"} />
        )}

        <div className={multiline ? "flex items-center gap-2" : "contents"}>
          <VariablePickerPopover
            variant={multiline ? "button" : "icon"}
            variables={catalog}
            isLoading={isLoading}
            isError={isError}
            ariaLabel={`Insert a configuration variable into ${ariaLabel.toLowerCase()}`}
            onPick={(variable) => insert(displayToken(variable, codec))}
          />
          {locked && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-8 text-xs"
              onClick={() => onChange("")}
            >
              Clear
            </Button>
          )}
        </div>
      </div>

      {!locked && hasRef(value, codec) && (
        <Badge variant="secondary" className="w-fit rounded px-1.5 py-0 text-[10px]">
          uses a variable
        </Badge>
      )}
    </div>
  );
};

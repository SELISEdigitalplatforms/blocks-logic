import { KeyRound, X } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { SecretListItem } from "@/models/secret";
import { useVariableCatalog } from "@/hooks/use-variable-catalog";
import { VariablePickerPopover } from "./variable-picker-popover";
import { VariableRefCodec, soleRefKey } from "./variable-ref";

export type VariableRefFieldProps = {
  value: string;
  onChange: (value: string) => void;
  codec: VariableRefCodec;
  /** Labels the text input, the picker trigger and the unbind button. */
  ariaLabel: string;
  /**
   * Supply the catalog instead of letting the field fetch it. For a form that already loads the
   * rows once and threads them down (Proxy), or a test that wants to control them.
   */
  variables?: SecretListItem[];
  isLoading?: boolean;
  isError?: boolean;
  placeholder?: string;
  hint?: string;
};

/**
 * A value that is either typed in plain, or bound to one of the tenant's configuration variables.
 *
 * Bound, it renders as a chip carrying the variable's **name**. The stored token's key — an opaque
 * id under `secretIdRef` — is never rendered, not even as a fallback when the catalog cannot name
 * it: an id is nothing a person can act on, and showing one only leaks it into screenshots and
 * tickets. An unresolvable reference says so in words and offers the way out, which is to clear it.
 *
 * Values are never shown because they are never fetched: `GET /api/Secret/GetAll` returns identity
 * and tags, and plaintext is resolved server-side at execution time.
 */
export const VariableRefField = ({
  value,
  onChange,
  codec,
  ariaLabel,
  variables,
  isLoading: isLoadingProp,
  isError: isErrorProp,
  placeholder,
  hint,
}: VariableRefFieldProps) => {
  // Skipped entirely when the caller supplies the rows, so a form that already has them does
  // not fetch the same list once per field.
  const query = useVariableCatalog({}, { enabled: !variables });
  const catalog = variables ?? query.data ?? [];
  const isLoading = isLoadingProp ?? (variables ? false : query.isLoading);
  const isError = isErrorProp ?? (variables ? false : query.isError);

  const boundKey = soleRefKey(value, codec);
  const bound = boundKey ? codec.find(boundKey, catalog) : undefined;
  // Until the catalog lands, a bound row is neither confirmed nor broken — it must not flash
  // "no longer exists" on every mount.
  const unresolved = Boolean(boundKey) && !bound && !isLoading;

  return (
    <div className="flex min-w-0 flex-col gap-1">
      <div className="flex min-w-0 items-center gap-1.5">
        {boundKey ? (
          <div
            data-testid="variable-binding"
            className={`flex h-9 min-w-0 flex-1 items-center gap-1.5 rounded-md border px-2 ${
              unresolved ? "border-error/40 bg-error/5" : "border-primary/30 bg-primary/5"
            }`}
          >
            <KeyRound
              className={`h-3.5 w-3.5 shrink-0 ${unresolved ? "text-error" : "text-primary"}`}
            />
            <span
              className={`min-w-0 flex-1 truncate text-xs ${
                unresolved ? "italic text-error" : "font-mono"
              }`}
            >
              {/* Never the key. There is no name to show, so say that, in words. */}
              {bound?.name ?? (isLoading ? "Loading…" : "Unavailable variable")}
            </span>
            {bound?.tags.slice(0, 1).map((tag) => (
              <Badge key={tag} variant="secondary" className="shrink-0 px-1.5 py-0 font-normal">
                {tag}
              </Badge>
            ))}
            <Button
              type="button"
              variant="ghost"
              size="icon"
              aria-label={`Unbind ${ariaLabel}`}
              className="h-6 w-6 shrink-0 text-medium-emphasis hover:text-error"
              onClick={() => onChange("")}
            >
              <X className="h-3.5 w-3.5" />
            </Button>
          </div>
        ) : (
          <Input
            aria-label={ariaLabel}
            placeholder={placeholder}
            className="h-9 min-w-0 flex-1 font-mono text-xs"
            value={value}
            onChange={(event) => onChange(event.target.value)}
          />
        )}
        <VariablePickerPopover
          variant="icon"
          variables={catalog}
          isLoading={isLoading}
          isError={isError}
          selectedId={bound?.id}
          ariaLabel={`Bind ${ariaLabel} to a configuration variable`}
          hint={hint}
          // Replaces rather than appends: the field holds a single binding, and half typed text
          // plus half reference could not be shown as a chip.
          onPick={(variable) => onChange(codec.build(variable))}
        />
      </div>

      {unresolved && (
        <p className="text-xs text-error">
          This configuration variable no longer exists. Pick another one, or clear it.
        </p>
      )}
    </div>
  );
};

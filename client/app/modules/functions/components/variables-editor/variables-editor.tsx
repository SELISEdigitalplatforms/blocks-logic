import { KeyRound, Plus, Trash2, TriangleAlert, X } from "lucide-react";
import { Input } from "@/components/ui-kits/input/input";
import { Button } from "@/components/ui-kits/button/button";
import { VariableRefField, secretIdRef, soleRefKey } from "@/components/variable-picker";
import { IVariableBinding } from "../../types/function.types";
import { isTypedCredential } from "../../utils/secret-literals";
import { soleSecretName } from "../../utils/transfer";
import { renderRichText } from "@/lib/rich-text";
import { useT, tStatic } from "@seliseblocks/genesis-os";

type VariablesEditorProps = {
  value: IVariableBinding[];
  onChange: (value: IVariableBinding[]) => void;
  /** Names with no secret here (server `missingSecrets`), for `{{secretName.X}}` values. */
  missingSecrets?: string[];
  /** Names that match more than one secret here (server `duplicateSecrets`). */
  duplicateSecrets?: string[];
};

/**
 * A value kept by secret NAME (`{{secretName.X}}`, left by an import until the secret exists). The
 * picker only knows ids, so it is shown read-only as the name with its state instead of as broken
 * text. Clear empties the value so a secret can be picked the normal way.
 */
const SecretNameValue = ({
  name,
  state,
  index,
  onClear,
}: {
  name: string;
  state: "missing" | "duplicate" | "bound";
  index: number;
  onClear: () => void;
}) => {
  const { t } = useT("logic.functions.components");
  return (
    <div
      className="flex h-9 min-w-0 items-center gap-2 rounded-md border bg-surface-app px-2.5 text-xs"
      data-testid={`variable-${index}-secret-name`}
    >
      <KeyRound className="h-3.5 w-3.5 shrink-0 text-medium-emphasis" />
      <span className="truncate font-mono">{name}</span>
      <span className={state === "bound" ? "text-medium-emphasis" : "text-warning-800"}>
        {state === "missing" ? t("VARIABLES_EDITOR.NOT_FOUND") : state === "duplicate" ? t("VARIABLES_EDITOR.MORE_THAN_ONE") : t("VARIABLES_EDITOR.BY_NAME")}
      </span>
      <button
        type="button"
        aria-label={t("VARIABLES_EDITOR.CLEAR_VARIABLE_VALUE", { value: index + 1 })}
        className="ml-auto shrink-0 text-medium-emphasis hover:text-foreground"
        onClick={onClear}
      >
        <X className="h-3.5 w-3.5" />
      </button>
    </div>
  );
};

const KEY_PATTERN = /^[A-Z][A-Z0-9_]*$/;
const MAX_VALUE_LENGTH = 4096;
/** A long opaque value under an ordinary name may still be a credential: warn, never refuse. */
const SECRET_LOOKING_VALUE = /^[A-Za-z0-9_\-.]{32,}$/;

const keyError = (key: string, index: number, all: IVariableBinding[]) => {
  if (!key) return tStatic("logic.functions.components", "VARIABLES_EDITOR.A_KEY_IS_REQUIRED");
  if (!KEY_PATTERN.test(key)) return tStatic("logic.functions.components", "VARIABLES_EDITOR.UPPER_CASE_DIGITS_AND");
  if (all.some((other, i) => i !== index && other.key === key)) return tStatic("logic.functions.components", "VARIABLES_EDITOR.THAT_KEY_IS_ALREADY");
  return null;
};

const valueError = (value: string) =>
  value.length > MAX_VALUE_LENGTH ? tStatic("logic.functions.components", "VARIABLES_EDITOR.THIS_VALUE_IS_TOO") : null;

const looksLikeSecret = ({ value }: IVariableBinding) => SECRET_LOOKING_VALUE.test(value);

/**
 * `ctx.env.KEY` bindings. A value is either typed in plain — readable by anyone who can see the
 * function's configuration — or bound to one of the tenant's platform configuration variables,
 * in which case only a `{{secret.<id>}}` reference is stored and the value is resolved on the
 * host as the run starts.
 */
export const VariablesEditor = ({
  value,
  onChange,
  missingSecrets = [],
  duplicateSecrets = [],
}: VariablesEditorProps) => {
  const { t } = useT("logic.functions.components");
  const update = (index: number, partial: Partial<IVariableBinding>) => {
    onChange(value.map((v, i) => (i === index ? { ...v, ...partial } : v)));
  };
  const remove = (index: number) => onChange(value.filter((_, i) => i !== index));
  const add = () => onChange([...value, { key: "", value: "" }]);

  return (
    <div className="flex flex-col">
      <div className="flex flex-wrap items-start justify-between gap-3 border-b px-4 py-3">
        <div className="flex flex-col gap-0.5">
          <span className="flex items-center gap-2 text-sm font-semibold">
            {renderRichText(t("VARIABLES_EDITOR.VARIABLES_0_0", { length: value.length }), [(children) => <span className="rounded-full bg-surface-app px-2 py-0.5 text-xs font-semibold text-medium-emphasis">{children}</span>])}
          </span>
          <span className="text-xs text-medium-emphasis">
            {renderRichText(t("VARIABLES_EDITOR.STRINGS_ON_0_CTX"), [(children) => <code className="font-mono">{children}</code>])}
          </span>
        </div>
        <Button type="button" size="sm" className="gap-1.5" onClick={add}>
          <Plus className="h-3.5 w-3.5" />
          {t("VARIABLES_EDITOR.ADD_VARIABLE")}
        </Button>
      </div>

      {value.length === 0 ? (
        <p className="px-4 py-8 text-center text-sm text-medium-emphasis">
          {renderRichText(t("VARIABLES_EDITOR.NOTHING_BOUND_YET_ADD"), [(children) => <code className="font-mono">{children}</code>])}
        </p>
      ) : (
        <>
          {/* Header and rows are separate grids: the last track is a fixed 32 px (the Remove
              button) rather than `auto`, which measured 0 in the header and 32 in each row and
              pushed every label out of line with its input. */}
          <div className="grid grid-cols-[minmax(0,1.1fr)_minmax(0,1.4fr)_minmax(0,0.9fr)_32px] gap-3 border-b px-4 py-2 text-xs font-semibold uppercase tracking-wide text-low-emphasis">
            <span>{t("VARIABLES_EDITOR.KEY")}</span>
            <span>{t("VARIABLES_EDITOR.VALUE")}</span>
            <span>{t("VARIABLES_EDITOR.READ_IN_CODE_AS")}</span>
            <span />
          </div>
          {value.map((variable, index) => {
            const keyMessage = keyError(variable.key, index, value);
            const valueMessage = valueError(variable.value);
            const byName = soleSecretName(variable.value);
            const bound = soleRefKey(variable.value, secretIdRef) ?? byName;
            // Same rule as the host's save check (FunctionSecretLiterals).
            const typedCredential = !bound && isTypedCredential(variable.key, variable.value);
            const secretWarning = !bound && !typedCredential && looksLikeSecret(variable);

            return (
              <div key={index} className="border-b px-4 py-2.5 last:border-b-0">
                <div className="grid grid-cols-[minmax(0,1.1fr)_minmax(0,1.4fr)_minmax(0,0.9fr)_32px] items-center gap-3">
                  <Input
                    aria-label={t("VARIABLES_EDITOR.VARIABLE_KEY", { value: index + 1 })}
                    placeholder="STRIPE_ACCOUNT"
                    className="h-9 font-mono text-xs"
                    value={variable.key}
                    onChange={(e) => update(index, { key: e.target.value.toUpperCase() })}
                  />

                  {byName ? (
                    <SecretNameValue
                      name={byName}
                      index={index}
                      state={
                        missingSecrets.includes(byName)
                          ? "missing"
                          : duplicateSecrets.includes(byName)
                            ? "duplicate"
                            : "bound"
                      }
                      onClear={() => update(index, { value: "" })}
                    />
                  ) : (
                    <VariableRefField
                      value={variable.value}
                      onChange={(next) => update(index, { value: next })}
                      codec={secretIdRef}
                      ariaLabel={t("VARIABLES_EDITOR.VARIABLE_VALUE", { value: index + 1 })}
                      placeholder="acct_1P9…"
                      hint={t("VARIABLES_EDITOR.STORED_AS_A_REFERENCE")}
                    />
                  )}

                  <code className="min-w-0 truncate font-mono text-xs text-medium-emphasis">
                    ctx.env.{variable.key || "NAME"}
                  </code>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label={t("VARIABLES_EDITOR.REMOVE_VARIABLE", { value: index + 1 })}
                    className="h-8 w-8 shrink-0 text-medium-emphasis hover:text-error"
                    onClick={() => remove(index)}
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>

                {(keyMessage || valueMessage) && (
                  <p className="mt-1.5 text-xs text-error">{keyMessage ?? valueMessage}</p>
                )}
                {!keyMessage && typedCredential && (
                  <p className="mt-1.5 flex items-start gap-1.5 text-xs text-warning-800">
                    <TriangleAlert className="mt-px h-3.5 w-3.5 shrink-0" />
                    {t("SECRET_LITERALS.TYPED_CREDENTIAL_MESSAGE")}
                  </p>
                )}
                {!keyMessage && secretWarning && (
                  <p className="mt-1.5 flex items-start gap-1.5 text-xs text-warning-800">
                    <TriangleAlert className="mt-px h-3.5 w-3.5 shrink-0" />
                    {t("VARIABLES_EDITOR.THIS_LOOKS_LIKE_A")}
                  </p>
                )}
              </div>
            );
          })}
        </>
      )}

      <p className="border-t px-4 py-3 text-xs text-medium-emphasis">
        {t("VARIABLES_EDITOR.CHANGES_APPLY_ON_THE")}
      </p>
    </div>
  );
};

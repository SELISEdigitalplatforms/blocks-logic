import { Plus, Trash2, TriangleAlert } from "lucide-react";
import { Input } from "@/components/ui-kits/input/input";
import { Button } from "@/components/ui-kits/button/button";
import { IVariableBinding } from "../../types/function.types";

type VariablesEditorProps = {
  value: IVariableBinding[];
  onChange: (value: IVariableBinding[]) => void;
};

const KEY_PATTERN = /^[A-Z][A-Z0-9_]*$/;
const MAX_VALUE_LENGTH = 4096;
/** Names that usually mean a credential — a variable is plain text on `ctx.env`, so warn, don't block. */
const SECRET_LOOKING_KEY = /(SECRET|TOKEN|PASSWORD|PASSWD|CREDENTIAL|PRIVATE|_KEY|^KEY$|APIKEY)/;
const SECRET_LOOKING_VALUE = /^[A-Za-z0-9_\-.]{32,}$/;

const keyError = (key: string, index: number, all: IVariableBinding[]) => {
  if (!key) return "A key is required.";
  if (!KEY_PATTERN.test(key)) return "Upper case, digits and underscore only; start with a letter.";
  if (all.some((other, i) => i !== index && other.key === key)) return "That key is already used.";
  return null;
};

const valueError = (value: string) =>
  value.length > MAX_VALUE_LENGTH ? `Values are capped at ${MAX_VALUE_LENGTH} characters.` : null;

const looksLikeSecret = ({ key, value }: IVariableBinding) =>
  SECRET_LOOKING_KEY.test(key.toUpperCase()) || SECRET_LOOKING_VALUE.test(value);

/** Non-secret `ctx.env.KEY` bindings. Secrets never go here — see the output actions editor. */
export const VariablesEditor = ({ value, onChange }: VariablesEditorProps) => {
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
            Variables
            <span className="rounded-full bg-surface-app px-2 py-0.5 text-xs font-semibold text-medium-emphasis">
              {value.length}
            </span>
          </span>
          <span className="text-xs text-medium-emphasis">
            Plain strings on <code className="font-mono">ctx.env</code>, snapshotted at deploy.
          </span>
        </div>
        <Button type="button" size="sm" className="gap-1.5" onClick={add}>
          <Plus className="h-3.5 w-3.5" />
          Add variable
        </Button>
      </div>

      {value.length === 0 ? (
        <p className="px-4 py-8 text-center text-sm text-medium-emphasis">
          Nothing bound yet — add a variable to read it as{" "}
          <code className="font-mono">ctx.env.NAME</code>.
        </p>
      ) : (
        <>
          <div className="grid grid-cols-[minmax(0,1.1fr)_minmax(0,1.2fr)_minmax(0,1fr)_auto] gap-3 border-b px-4 py-2 text-xs font-semibold uppercase tracking-wide text-low-emphasis">
            <span>Key</span>
            <span>Value</span>
            <span>Read in code as</span>
            <span />
          </div>
          {value.map((variable, index) => {
            const keyMessage = keyError(variable.key, index, value);
            const valueMessage = valueError(variable.value);
            const secretWarning = looksLikeSecret(variable);
            return (
              <div key={index} className="border-b px-4 py-2.5 last:border-b-0">
                <div className="grid grid-cols-[minmax(0,1.1fr)_minmax(0,1.2fr)_minmax(0,1fr)_auto] items-center gap-3">
                  <Input
                    aria-label={`Variable ${index + 1} key`}
                    placeholder="STRIPE_ACCOUNT"
                    className="h-9 font-mono text-xs"
                    value={variable.key}
                    onChange={(e) => update(index, { key: e.target.value.toUpperCase() })}
                  />
                  <Input
                    aria-label={`Variable ${index + 1} value`}
                    placeholder="acct_1P9…"
                    className="h-9 font-mono text-xs"
                    value={variable.value}
                    onChange={(e) => update(index, { value: e.target.value })}
                  />
                  <code className="min-w-0 truncate font-mono text-xs text-medium-emphasis">
                    ctx.env.{variable.key || "NAME"}
                  </code>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label={`Remove variable ${index + 1}`}
                    className="h-8 w-8 shrink-0 text-medium-emphasis hover:text-error"
                    onClick={() => remove(index)}
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
                {(keyMessage || valueMessage) && (
                  <p className="mt-1.5 text-xs text-error">{keyMessage ?? valueMessage}</p>
                )}
                {!keyMessage && secretWarning && (
                  <p className="mt-1.5 flex items-start gap-1.5 text-xs text-warning-800">
                    <TriangleAlert className="mt-px h-3.5 w-3.5 shrink-0" />
                    This looks like a secret. Store it in Blocks OS Secrets and use it in an output
                    action instead — variables are readable in the sandbox.
                  </p>
                )}
              </div>
            );
          })}
        </>
      )}

      <p className="border-t px-4 py-3 text-xs text-medium-emphasis">
        Changes apply on the next deploy, so a version always runs with the variables it was built
        with.
      </p>
    </div>
  );
};

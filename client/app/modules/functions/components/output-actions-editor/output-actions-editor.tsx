import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import { Input } from "@/components/ui-kits/input/input";
import { Button } from "@/components/ui-kits/button/button";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Card } from "@/components/ui-kits/card/card";
import { Switch } from "@/components/ui-kits/switch/switch";
import { cn } from "@/lib/utils";
import { HTTP_METHOD_OPTIONS } from "../../constants/limits.constant";
import { IOutputAction } from "../../types/function.types";
import { VariableTokenField, secretIdRef } from "@/components/variable-picker";
import { isTypedCredential } from "../../utils/secret-literals";
import { renderRichText } from "@/lib/rich-text";
import { useT } from "@seliseblocks/genesis-os";

type OutputActionsEditorProps = {
  value: IOutputAction[];
  onChange: (value: IOutputAction[]) => void;
};

/**
 * Methods this action will actually send a body with. Same rule as the proxy's routes card, and
 * as `OutputActionProcessor.MethodTakesBody` on the host: RFC 9110 gives a GET payload no defined
 * semantics, so offering a body for one promises something the call will not do.
 */
const hasBody = (method: string) => method === "POST" || method === "PUT" || method === "PATCH";

const TIMEOUT_OPTIONS = [5, 10, 15, 20, 30];

const newOutputAction = (): IOutputAction => ({
  id: crypto.randomUUID(),
  kind: "ExternalHttp",
  enabled: true,
  url: "",
  method: "POST",
  headers: {},
  bodyTemplate: null,
  timeoutSeconds: 30,
});

/**
 * Output actions run on the Blocks host after the run finishes, which is the whole point of the tab:
 * they are the only place a secret is resolved, and the sandbox never sees one. A `{{secret.NAME}}`
 * placeholder is all this editor ever holds.
 */
export const OutputActionsEditor = ({ value, onChange }: OutputActionsEditorProps) => {
  const { t } = useT("logic.functions.components");
  const update = (index: number, partial: Partial<IOutputAction>) => {
    onChange(value.map((action, i) => (i === index ? { ...action, ...partial } : action)));
  };
  const remove = (index: number) => onChange(value.filter((_, i) => i !== index));
  const add = () => onChange([...value, newOutputAction()]);

  const move = (index: number, offset: number) => {
    const target = index + offset;
    if (target < 0 || target >= value.length) return;
    const next = [...value];
    [next[index], next[target]] = [next[target], next[index]];
    onChange(next);
  };

  const updateHeader = (index: number, key: string, headerValue: string) => {
    update(index, { headers: { ...value[index].headers, [key]: headerValue } });
  };
  const renameHeader = (index: number, from: string, to: string) => {
    const headers: Record<string, string> = {};
    Object.entries(value[index].headers).forEach(([key, headerValue]) => {
      headers[key === from ? to : key] = headerValue;
    });
    update(index, { headers });
  };
  const removeHeader = (index: number, key: string) => {
    const headers = { ...value[index].headers };
    delete headers[key];
    update(index, { headers });
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <p className="max-w-[66ch] text-xs leading-relaxed text-medium-emphasis">
          {t("OUTPUT_ACTIONS_EDITOR.WHAT_HAPPENS_TO_THE")}
        </p>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="shrink-0 gap-1.5"
          onClick={add}
        >
          <Plus className="h-3.5 w-3.5" />
          {t("OUTPUT_ACTIONS_EDITOR.ADD_ACTION")}
        </Button>
      </div>

      {value.length === 0 && (
        <div className="flex flex-col gap-1 rounded-lg border border-dashed px-5 py-8 text-center">
          <span className="text-sm font-semibold">
            {t("OUTPUT_ACTIONS_EDITOR.NO_OUTPUT_ACTIONS")}
          </span>
          <span className="text-xs text-medium-emphasis">
            {renderRichText(t("OUTPUT_ACTIONS_EDITOR.RETURN_VALUE_STORED"), [
              (text) => <code className="font-mono">{text}</code>,
            ])}
          </span>
        </div>
      )}

      {value.map((action, index) => {
        // Missing means on: the host's OutputAction.Enabled defaults to true.
        const isEnabled = action.enabled !== false;
        return (
          <Card key={action.id} className="overflow-hidden">
            <div className="flex items-center gap-3 border-b bg-surface-app px-4 py-3">
              <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-blocks-primary-50 text-xs font-semibold text-primary">
                {index + 1}
              </span>
              <span className="min-w-0 truncate text-sm font-semibold">
                {t("OUTPUT_ACTIONS_EDITOR.EXTERNAL_HTTP_CALL")}
              </span>
              {!isEnabled && (
                <span className="shrink-0 rounded bg-neutral-100 px-1.5 py-0.5 text-[11px] font-medium text-medium-emphasis">
                  {t("OUTPUT_ACTIONS_EDITOR.OFF_SKIPPED")}
                </span>
              )}
              <span className="flex-1" />
              {/* The host skips a disabled action (FN-48); turning one off keeps its settings. */}
              <Switch
                aria-label={t("OUTPUT_ACTIONS_EDITOR.ACTION_ON", { value: index + 1 })}
                checked={isEnabled}
                onCheckedChange={(checked) => update(index, { enabled: checked })}
                className="flex-shrink-0"
              />
              <Button
                type="button"
                variant="ghost"
                size="icon"
                aria-label={t("OUTPUT_ACTIONS_EDITOR.MOVE_EARLIER")}
                disabled={index === 0}
                className="h-7 w-7 disabled:opacity-30"
                onClick={() => move(index, -1)}
              >
                <ArrowUp className="h-3.5 w-3.5" />
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                aria-label={t("OUTPUT_ACTIONS_EDITOR.MOVE_LATER")}
                disabled={index === value.length - 1}
                className="h-7 w-7 disabled:opacity-30"
                onClick={() => move(index, 1)}
              >
                <ArrowDown className="h-3.5 w-3.5" />
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="h-7 gap-1.5 px-2 text-xs text-error hover:text-error"
                onClick={() => remove(index)}
              >
                <Trash2 className="h-3.5 w-3.5" />
                {t("common:REMOVE")}
              </Button>
            </div>

            <div className={cn("flex flex-col gap-3.5 p-4", !isEnabled && "opacity-60")}>
              <div className="flex flex-wrap items-end gap-3">
                <div className="flex flex-col gap-1.5">
                  <Label className="text-xs font-semibold">
                    {t("OUTPUT_ACTIONS_EDITOR.METHOD")}
                  </Label>
                  <Select value={action.method} onValueChange={(v) => update(index, { method: v })}>
                    <SelectTrigger className="h-9 w-28">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {HTTP_METHOD_OPTIONS.map((method) => (
                        <SelectItem key={method} value={method}>
                          {method}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <div className="flex min-w-[220px] flex-1 flex-col gap-1.5">
                  <Label className="text-xs font-semibold">
                    {t("OUTPUT_ACTIONS_EDITOR.ENDPOINT")}
                  </Label>
                  <Input
                    className="h-9 font-mono text-xs"
                    placeholder="https://api.vendor.com/v1/events"
                    value={action.url}
                    onChange={(e) => update(index, { url: e.target.value })}
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label className="text-xs font-semibold">
                    {t("OUTPUT_ACTIONS_EDITOR.TIMEOUT")}
                  </Label>
                  <Select
                    value={String(action.timeoutSeconds)}
                    onValueChange={(v) => update(index, { timeoutSeconds: Number(v) })}
                  >
                    <SelectTrigger className="h-9 w-28">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {TIMEOUT_OPTIONS.map((seconds) => (
                        <SelectItem key={seconds} value={String(seconds)}>
                          {seconds} s
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              </div>

              <div className="flex flex-col gap-2">
                <div className="flex items-center justify-between gap-3">
                  <Label className="text-xs font-semibold">
                    {t("OUTPUT_ACTIONS_EDITOR.HEADERS")}
                  </Label>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="h-8 gap-1 px-2 text-xs text-primary hover:text-primary"
                    onClick={() => updateHeader(index, "", "")}
                  >
                    <Plus className="h-3.5 w-3.5" />
                    {t("OUTPUT_ACTIONS_EDITOR.ADD_HEADER")}
                  </Button>
                </div>
                {Object.entries(action.headers).length === 0 && (
                  <p className="text-xs text-medium-emphasis">
                    {renderRichText(t("OUTPUT_ACTIONS_EDITOR.NO_HEADERS_A_POST"), [
                      (children) => <code className="font-mono">{children}</code>,
                    ])}
                  </p>
                )}
                {Object.entries(action.headers).map(([key, headerValue]) => (
                  <div key={key} className="flex flex-col gap-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <Input
                        aria-label={t("OUTPUT_ACTIONS_EDITOR.HEADER_NAME")}
                        placeholder={t("OUTPUT_ACTIONS_EDITOR.HEADER_NAME")}
                        className="h-9 min-w-[140px] flex-[0_1_190px] font-mono text-xs"
                        value={key}
                        onChange={(e) => renameHeader(index, key, e.target.value)}
                      />
                      <div className="min-w-[180px] flex-1">
                        <VariableTokenField
                          value={headerValue}
                          onChange={(next) => updateHeader(index, key, next)}
                          codec={secretIdRef}
                          ariaLabel={t("OUTPUT_ACTIONS_EDITOR.HEADER_VALUE")}
                          placeholder={t("OUTPUT_ACTIONS_EDITOR.VALUE")}
                        />
                      </div>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        aria-label={t("OUTPUT_ACTIONS_EDITOR.REMOVE_HEADER")}
                        className="h-8 w-8 shrink-0 text-medium-emphasis hover:text-error"
                        onClick={() => removeHeader(index, key)}
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>
                    {/* Same rule as the host's save check (FunctionSecretLiterals). */}
                    {isTypedCredential(key, headerValue) && (
                      <p className="text-xs text-warning-800">
                        {t("SECRET_LITERALS.TYPED_CREDENTIAL_MESSAGE")}
                      </p>
                    )}
                  </div>
                ))}
              </div>

              {!hasBody(action.method) ? (
                <p className="text-xs text-medium-emphasis">
                  {renderRichText(
                    t("OUTPUT_ACTIONS_EDITOR.A_0_0_CARRIES", { method: action.method }),
                    [(children) => <code className="font-mono">{children}</code>],
                  )}
                </p>
              ) : (
                <div className="flex flex-col gap-2">
                  <Label className="text-xs font-semibold" id={`fn-body-mode-${action.id}`}>
                    {t("OUTPUT_ACTIONS_EDITOR.BODY")}
                  </Label>
                  <div
                    className="flex flex-wrap gap-2"
                    role="radiogroup"
                    aria-labelledby={`fn-body-mode-${action.id}`}
                  >
                    {[
                      { label: t("OUTPUT_ACTIONS_EDITOR.FUNCTION_RESULT"), isTemplate: false },
                      { label: t("OUTPUT_ACTIONS_EDITOR.TEMPLATE"), isTemplate: true },
                    ].map((option) => {
                      const isSelected = (action.bodyTemplate != null) === option.isTemplate;
                      return (
                        <button
                          key={option.label}
                          type="button"
                          role="radio"
                          aria-checked={isSelected}
                          className={cn(
                            "rounded-md border px-3 py-1.5 text-xs font-semibold transition-colors",
                            isSelected
                              ? "border-primary bg-blocks-primary-25 text-primary"
                              : "border-border text-medium-emphasis hover:bg-surface-app",
                          )}
                          onClick={() =>
                            update(index, { bodyTemplate: option.isTemplate ? "{{result}}" : null })
                          }
                        >
                          {option.label}
                        </button>
                      );
                    })}
                  </div>
                  {action.bodyTemplate == null ? (
                    <p className="text-xs text-medium-emphasis">
                      {t("OUTPUT_ACTIONS_EDITOR.THE_RETURNED_VALUE_IS")}
                    </p>
                  ) : (
                    <>
                      <VariableTokenField
                        multiline
                        value={action.bodyTemplate}
                        onChange={(next) => update(index, { bodyTemplate: next })}
                        codec={secretIdRef}
                        ariaLabel={t("OUTPUT_ACTIONS_EDITOR.BODY_TEMPLATE")}
                        placeholder={'{ "payload": {{result}} }'}
                        className="min-h-[90px] resize-y font-mono text-xs"
                      />
                      <p className="text-xs text-medium-emphasis">
                        <code className="font-mono">{"{{result}}"}</code> is replaced with the
                        returned value before the call.
                      </p>
                    </>
                  )}
                </div>
              )}
            </div>
          </Card>
        );
      })}

      {value.length > 0 && (
        <p className="text-xs text-medium-emphasis">
          {t("OUTPUT_ACTIONS_EDITOR.CONFIGURATION_VARIABLES_ARE_RESOLVED")}
        </p>
      )}
    </div>
  );
};

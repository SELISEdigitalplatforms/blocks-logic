import { useState } from "react";
import { TriangleAlert, X } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { cn } from "@/lib/utils";
import { AUTH_MODE_OPTIONS, MATCH_MODE_OPTIONS } from "../../constants/limits.constant";
import { AuthMode, ITriggerConfig, MatchMode } from "../../types/function.types";
import { EndpointBadge } from "../endpoint-badge";

type ChipListProps = {
  label: string;
  values: string[];
  onChange: (values: string[]) => void;
  placeholder: string;
  mono?: boolean;
};

const ChipList = ({ label, values, onChange, placeholder, mono }: ChipListProps) => {
  const [draft, setDraft] = useState("");

  const commit = () => {
    const trimmed = draft.trim();
    if (trimmed && !values.includes(trimmed)) onChange([...values, trimmed]);
    setDraft("");
  };

  return (
    <div className="flex flex-col gap-2">
      <span className="text-xs font-medium uppercase tracking-wide text-medium-emphasis">
        {label}
      </span>
      <div className="flex flex-wrap items-center gap-2">
        {values.map((value) => (
          <span
            key={value}
            className={cn(
              "flex items-center gap-1.5 rounded-full bg-blocks-primary-50 px-2.5 py-1 text-xs font-semibold text-primary",
              mono && "font-mono",
            )}
          >
            {value}
            <button
              type="button"
              aria-label={`Remove ${value}`}
              className="text-medium-emphasis hover:text-error"
              onClick={() => onChange(values.filter((v) => v !== value))}
            >
              <X className="h-3 w-3" />
            </button>
          </span>
        ))}
        <Input
          aria-label={`Add ${label.toLowerCase()}`}
          placeholder={placeholder}
          className="h-8 w-44 rounded-full border-dashed text-xs"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter" || e.key === ",") {
              e.preventDefault();
              commit();
            }
            // Committing on blur means abandoned text still becomes a chip, so there has to be a
            // way to abandon it deliberately: Esc drops the draft, and the blur that follows sees
            // nothing to commit.
            if (e.key === "Escape") {
              e.preventDefault();
              setDraft("");
            }
          }}
          onBlur={commit}
        />
      </div>
    </div>
  );
};

const summarise = ({ roles, permissions, roleMatch }: ITriggerConfig) => {
  if (roles.length + permissions.length === 0)
    return "No extra restriction — any signed-in caller with a valid Blocks token can invoke it.";
  return roleMatch === "All"
    ? "AND — the caller must hold every listed role and permission."
    : "OR — the caller needs at least one of the listed roles or permissions.";
};

type TriggerHttpCardProps = {
  value: ITriggerConfig;
  onChange: (value: ITriggerConfig) => void;
  functionId: string;
};

/**
 * HTTP is always on — there is no switch, because a function with no endpoint has no way in. The
 * only decision here is who may call it, and the two match modes are one control: the design has a
 * single OR/AND choice covering roles and permissions together.
 */
export const TriggerHttpCard = ({ value, onChange, functionId }: TriggerHttpCardProps) => {
  const patch = (partial: Partial<ITriggerConfig>) => onChange({ ...value, ...partial });
  const setMatch = (match: MatchMode) => patch({ roleMatch: match, permissionMatch: match });

  return (
    <Card>
      <CardContent className="flex flex-col gap-4 p-5">
        <div className="flex flex-col gap-1">
          <span className="text-base font-semibold">HTTP endpoint</span>
          <span className="text-xs leading-relaxed text-medium-emphasis">
            Always on. The call returns <code className="font-mono">202 Accepted</code> with a run
            id — the code runs on the sandbox host, so nothing is held open waiting.
          </span>
        </div>

        <EndpointBadge functionId={functionId} />

        <div className="flex flex-col gap-2">
          <span className="text-xs font-semibold" id="fn-auth-mode-label">
            Who can call it
          </span>
          {/*
            An exclusive choice, so radio semantics rather than aria-pressed: a toggle button
            announces "pressed/not pressed" per option and never says one of two.
          */}
          <div
            className="flex flex-col gap-2"
            role="radiogroup"
            aria-labelledby="fn-auth-mode-label"
          >
            {AUTH_MODE_OPTIONS.map((option) => {
              const isSelected = value.authMode === option.value;
              return (
                <button
                  key={option.value}
                  type="button"
                  role="radio"
                  aria-checked={isSelected}
                  className={cn(
                    "flex items-start gap-2.5 rounded-lg border p-3 text-left transition-colors",
                    isSelected
                      ? "border-primary bg-blocks-primary-25"
                      : "border-border hover:bg-surface-app",
                  )}
                  onClick={() => patch({ authMode: option.value as AuthMode })}
                >
                  <span
                    className={cn(
                      "mt-0.5 h-3.5 w-3.5 shrink-0 rounded-full border bg-background",
                      isSelected ? "border-[4px] border-primary" : "border-border-medium-emphasis",
                    )}
                  />
                  <span className="flex min-w-0 flex-col gap-0.5">
                    <span className="text-xs font-semibold">{option.label}</span>
                    <span className="text-xs leading-relaxed text-medium-emphasis">
                      {option.hint}
                    </span>
                  </span>
                </button>
              );
            })}
          </div>
        </div>

        {value.authMode === "Token" && (
          <div className="flex flex-col gap-3 rounded-lg border bg-surface-app p-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <span className="text-xs font-semibold">
                Restrict further{" "}
                <span className="font-normal text-medium-emphasis">
                  — optional, both work together
                </span>
              </span>
              <div
                className="flex overflow-hidden rounded-md border"
                role="radiogroup"
                aria-label="How roles and permissions combine"
              >
                {MATCH_MODE_OPTIONS.map((option) => (
                  <button
                    key={option.value}
                    type="button"
                    role="radio"
                    aria-checked={value.roleMatch === option.value}
                    className={cn(
                      "px-3 py-1.5 text-xs font-semibold transition-colors",
                      value.roleMatch === option.value
                        ? "bg-primary text-primary-foreground"
                        : "bg-background text-medium-emphasis hover:bg-surface-app",
                    )}
                    onClick={() => setMatch(option.value as MatchMode)}
                  >
                    {option.label}
                  </button>
                ))}
              </div>
            </div>

            <ChipList
              label="Roles"
              values={value.roles}
              onChange={(roles) => patch({ roles })}
              placeholder="＋ Add role"
            />
            <ChipList
              label="Permissions"
              mono
              values={value.permissions}
              onChange={(permissions) => patch({ permissions })}
              placeholder="＋ Add permission"
            />

            <p className="text-xs leading-relaxed text-medium-emphasis">{summarise(value)}</p>
          </div>
        )}

        {value.authMode === "Public" && (
          <div className="flex items-start gap-2.5 rounded-lg border border-error/30 bg-error/5 p-3">
            <TriangleAlert className="mt-px h-4 w-4 shrink-0 text-error" />
            <span className="text-xs leading-relaxed text-error">
              Anonymous callers get no identity:{" "}
              <code className="font-mono">ctx.context.isAuthenticated</code> is{" "}
              <code className="font-mono">false</code> and <code className="font-mono">userId</code>{" "}
              is <code className="font-mono">null</code>. Roles and permissions don&apos;t apply,
              and nothing token-scoped will work.
            </span>
          </div>
        )}
      </CardContent>
    </Card>
  );
};

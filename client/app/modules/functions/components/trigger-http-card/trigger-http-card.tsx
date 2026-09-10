import { useState } from "react";
import { X } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui-kits/card/card";
import { Switch } from "@/components/ui-kits/switch/switch";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { Badge } from "@/components/ui-kits/badge/badge";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { AUTH_MODE_OPTIONS, MATCH_MODE_OPTIONS } from "../../constants/limits.constant";
import { ITriggerConfig } from "../../types/function.types";
import { EndpointBadge } from "../endpoint-badge";

type TagListInputProps = {
  values: string[];
  onChange: (values: string[]) => void;
  placeholder: string;
};

const TagListInput = ({ values, onChange, placeholder }: TagListInputProps) => {
  const [draft, setDraft] = useState("");

  const commit = () => {
    const trimmed = draft.trim();
    if (trimmed && !values.includes(trimmed)) onChange([...values, trimmed]);
    setDraft("");
  };

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-1.5">
        {values.map((tag) => (
          <Badge key={tag} variant="secondary" className="gap-1 rounded-md px-2 py-1 font-mono">
            {tag}
            <button type="button" onClick={() => onChange(values.filter((v) => v !== tag))}>
              <X className="h-3 w-3" />
            </button>
          </Badge>
        ))}
      </div>
      <Input
        placeholder={placeholder}
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === ",") {
            e.preventDefault();
            commit();
          }
        }}
        onBlur={commit}
      />
    </div>
  );
};

type TriggerHttpCardProps = {
  value: ITriggerConfig;
  onChange: (value: ITriggerConfig) => void;
  functionId: string;
};

export const TriggerHttpCard = ({ value, onChange, functionId }: TriggerHttpCardProps) => {
  const patch = (partial: Partial<ITriggerConfig>) => onChange({ ...value, ...partial });

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between pb-3">
        <div>
          <CardTitle className="text-base">HTTP trigger</CardTitle>
          <CardDescription>Invoke this function directly over HTTP.</CardDescription>
        </div>
        <Switch checked={value.httpEnabled} onCheckedChange={(checked) => patch({ httpEnabled: checked })} />
      </CardHeader>
      {value.httpEnabled && (
        <CardContent className="space-y-4">
          <EndpointBadge functionId={functionId} />

          <div className="space-y-1.5">
            <Label>Authentication</Label>
            <Select value={value.authMode} onValueChange={(v) => patch({ authMode: v as ITriggerConfig["authMode"] })}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {AUTH_MODE_OPTIONS.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {value.authMode === "Token" && (
            <div className="grid grid-cols-1 gap-4 border-t pt-4 sm:grid-cols-2">
              <div className="space-y-1.5">
                <div className="flex items-center justify-between">
                  <Label>Roles</Label>
                  <Select
                    value={value.roleMatch}
                    onValueChange={(v) => patch({ roleMatch: v as ITriggerConfig["roleMatch"] })}
                  >
                    <SelectTrigger className="h-7 w-20 text-xs">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {MATCH_MODE_OPTIONS.map((option) => (
                        <SelectItem key={option.value} value={option.value}>
                          {option.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <TagListInput
                  values={value.roles}
                  onChange={(roles) => patch({ roles })}
                  placeholder="Add a role, press Enter"
                />
              </div>
              <div className="space-y-1.5">
                <div className="flex items-center justify-between">
                  <Label>Permissions</Label>
                  <Select
                    value={value.permissionMatch}
                    onValueChange={(v) => patch({ permissionMatch: v as ITriggerConfig["permissionMatch"] })}
                  >
                    <SelectTrigger className="h-7 w-20 text-xs">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {MATCH_MODE_OPTIONS.map((option) => (
                        <SelectItem key={option.value} value={option.value}>
                          {option.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <TagListInput
                  values={value.permissions}
                  onChange={(permissions) => patch({ permissions })}
                  placeholder="Add a permission, press Enter"
                />
              </div>
            </div>
          )}
        </CardContent>
      )}
    </Card>
  );
};

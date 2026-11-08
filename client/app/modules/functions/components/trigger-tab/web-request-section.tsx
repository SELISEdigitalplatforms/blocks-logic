import { Globe } from "lucide-react";
import { useT } from "@seliseblocks/genesis-os";
import { ITriggerConfig } from "../../types/function.types";
import { CallItBlock } from "../call-it-block";
import { RateLimitField } from "../rate-limit-field";
import { TriggerHttpFields } from "../trigger-http-fields";
import { TriggerSection } from "./trigger-section";

type WebRequestSectionProps = {
  functionId: string;
  trigger: ITriggerConfig;
  onTriggerChange: (value: ITriggerConfig) => void;
  requestsPerMinute: number | null | undefined;
  onRequestsPerMinuteChange: (value: number | null) => void;
};

/** "Web request": call this function from any app or website. */
export const WebRequestSection = ({
  functionId,
  trigger,
  onTriggerChange,
  requestsPerMinute,
  onRequestsPerMinuteChange,
}: Readonly<WebRequestSectionProps>) => {
  const { t } = useT("logic.functions.components");

  return (
    <TriggerSection
      icon={Globe}
      title={t("TRIGGER_TAB.WEB_REQUEST")}
      subtitle={t("TRIGGER_TAB.WEB_REQUEST_SUBTITLE")}
      checked={trigger.httpEnabled}
      onCheckedChange={(httpEnabled) => onTriggerChange({ ...trigger, httpEnabled })}
      switchLabel={t("TRIGGER_TAB.ALLOW_WEB_REQUESTS")}
      toggleLabel={t("TRIGGER_TAB.SHOW_OR_HIDE_WEB_REQUEST")}
    >
      <div className="flex flex-col gap-6">
        <TriggerHttpFields value={trigger} onChange={onTriggerChange} functionId={functionId} />
        <RateLimitField
          key={functionId}
          authMode={trigger.authMode}
          value={requestsPerMinute}
          onChange={onRequestsPerMinuteChange}
        />
        <CallItBlock functionId={functionId} trigger={trigger} />
      </div>
    </TriggerSection>
  );
};

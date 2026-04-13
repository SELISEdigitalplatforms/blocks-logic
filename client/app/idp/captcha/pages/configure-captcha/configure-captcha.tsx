

import { LogMenu } from "@blocks-lmt/components";
import { ConfigureCaptchaModal } from "../../modals/configure-captcha-modal/";
import { ConfigureCaptchaList } from "./configure-captcha-list";
import { useProjectStore } from "@/store/useProjectStore";
import { useGetCaptchaConfigs } from "../../hooks/use-captcha-config";
import { DialogTrigger } from "@radix-ui/react-dialog";
import { Button } from "@/components/ui-kits/button/button";
import { CirclePlus } from "lucide-react";
import { MouseEvent, useMemo } from "react";
import { CAPTCHA_PROVIDERS, CAPTCHA_PROVIDERS_KEY } from "../../models/captcha";
import { toast } from "@/hooks/use-toast";

export const ConfigureCaptcha = () => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { isLoading, isFetching, data } = useGetCaptchaConfigs({ projectKey: tenantId });

  const areAllProvidersConfigured = useMemo(() => {
    if (!data?.configurations) return false;
    const allProviderKeys = Object.keys(CAPTCHA_PROVIDERS) as CAPTCHA_PROVIDERS_KEY[];
    const configuredProviders = new Set(
      data.configurations.map((config: { provider: string }) => config.provider),
    );
    return allProviderKeys.every((key) => configuredProviders.has(key));
  }, [data]);

  const addConfigurationHandler = (e: MouseEvent) => {
    if (areAllProvidersConfigured) {
      toast({
        variant: "info",
        title: "Info",
        description: "No additional captcha configurations can be added.",
      });
      return e.preventDefault();
    }
  };

  return (
    <div>
      <div className="mb-[18px] flex items-center justify-between md:mb-[24px]">
        <div className="item-center flex gap-2">
          <h1 className="text-lg font-semibold md:text-2xl">CAPTCHA</h1>
        </div>
        <div className="flex items-center gap-2 md:gap-4">
          <Button
            size="sm"
            variant="outline"
            onClick={() =>
              window.open(
                `${import.meta.env.BLOCKS_API_BASE_URL}/idp/v1/swagger/index.html`,
                "_blank",
              )
            }
          >
            API Docs
          </Button>
          <LogMenu link="/services/captcha/logs" />
          <ConfigureCaptchaModal>
            <DialogTrigger asChild>
              <Button size="sm" onClick={addConfigurationHandler}>
                <CirclePlus className="h-5 w-5" />
                <span className="sr-only sm:not-sr-only sm:ml-2.5">Add Configuration</span>
              </Button>
            </DialogTrigger>
          </ConfigureCaptchaModal>
        </div>
      </div>
      <ConfigureCaptchaList
        isLoading={isLoading || isFetching}
        configurations={data?.configurations || []}
      />
    </div>
  );
};

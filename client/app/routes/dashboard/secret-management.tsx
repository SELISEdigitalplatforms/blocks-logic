import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { TabsContent } from "@radix-ui/react-tabs";
import { useQueryState } from "nuqs";
import { SSO } from "@blocks-idp/authentication/pages/authentication-config/sso";
import { GRANT_TYPES, SecretManagementTabs } from "@blocks-idp/authentication/constants/authentication.constant";
import { OIDC } from "@blocks-idp/authentication/components/oidc";
import { Certificates } from "@blocks-idp/authentication/pages/authentication-config/general/certificates/certificates";
import { CreateOIDC } from "@blocks-idp/authentication/components/create-oidc";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui-kits/select/select";

export default function SecretManagementPage() {
  const [selectedTab, setSelectedTab] = useQueryState("tab", { defaultValue: GRANT_TYPES.authorizationCode });

  return (
    <div className="p-6">
      <div className="mb-[18px] flex items-center justify-between md:mb-[24px]">
        <h1 className="text-lg font-semibold md:text-2xl">Secret Management</h1>
      </div>
      <Tabs value={selectedTab} onValueChange={(value) => setSelectedTab(value)}>
        <div className="mb-4 flex items-start justify-between gap-4">
          <>
            <TabsList className="hidden w-auto sm:inline-flex">
              {SecretManagementTabs.map((item) => (
                <TabsTrigger key={item.id} value={item.value}>
                  {item.label}
                </TabsTrigger>
              ))}
            </TabsList>
            <div className="sm:hidden">
              <Select value={selectedTab} onValueChange={(value) => setSelectedTab(value)}>
                <SelectTrigger className="w-32 gap-2">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {SecretManagementTabs.map((item) => (
                    <SelectItem key={item.id} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </>
          <>
            {selectedTab === GRANT_TYPES.authorizationCode && <CreateOIDC />}
          </>
        </div>
        <TabsContent value={GRANT_TYPES.authorizationCode}>
          <OIDC />
        </TabsContent>
        <TabsContent value={GRANT_TYPES.social}>
          <SSO />
        </TabsContent>
        <TabsContent value="external-idp">
          <Certificates />
        </TabsContent>
      </Tabs>
    </div>
  );
}


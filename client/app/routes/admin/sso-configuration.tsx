import { useParams } from "react-router-dom";
import { SSOConfiguration } from "@blocks-idp/authentication/pages/sso-configuration";

export default function SsoConfigurationPage() {
  const { provider, id } = useParams<{ provider: string; id: string }>();
  return <SSOConfiguration params={{ provider: provider as any, id: id || "" }} />;
}

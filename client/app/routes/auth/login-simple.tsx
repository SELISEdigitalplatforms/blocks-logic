import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Logo } from "@/components/logo";
import { BlockInfo } from "@blocks-idp/authentication/components/auth-layout/blocks-info";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { ShieldCheck, Users, KeyRound, Puzzle } from "lucide-react";

const features = [
  { icon: ShieldCheck, label: "MFA & Passkeys" },
  { icon: Users, label: "User Management" },
  { icon: KeyRound, label: "OAuth 2.0 / OIDC" },
  { icon: Puzzle, label: "Modular Services" },
];

export default function LoginSimplePage() {
  const [isLoading, setIsLoading] = useState(false);

  const handleLogin = async () => {
    setIsLoading(true);
    try {
      const blocksKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");

      const params = new URLSearchParams({
        response_type: "code",
        client_id: "44ce2f9b-0ca4-4ad8-b8d4-bb775b61d68e",
        redirect_uri: "http://localhost:4000/oidc",
        scope: "openId",
        audience: "http://localhost:4000",
        state: "039849038",
        nonce: "35443",
      });

      const response = await fetch(
        `https://dev-api.blocksdevelopers.com/idp/v1/Authentication/Authorize?${params.toString()}`,
        {
          method: "GET",
          headers: {
            accept: "*/*",
            "x-blocks-key": blocksKey,
          },
        },
      );

      if(response.status === 302) {
        console.log(response);
        console.log(response.url);
      }


      if (!response.ok) return;

      const contentType = response.headers.get("content-type") ?? "";
      if (contentType.includes("application/json")) {
        const data = await response.json();
        const redirectTo: string | undefined =
          data.redirectUrl ?? data.providerUrl ?? data.url ?? data.redirect_uri;
        if (redirectTo) window.location.href = redirectTo;
      }
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="flex min-h-screen flex-col items-center py-[24px] lg:py-[64px] xl:px-[154px]">
      <div className="flex w-full items-center">
        <Logo width={128} height={55} />
      </div>

      <div className="mt-[20px] flex w-full flex-col justify-center gap-0 md:px-[24px] lg:mt-[70px] lg:flex-row lg:gap-20 lg:px-0 2xl:mt-[80px]">
        {/* Hero left column */}
        <div className="flex flex-1 flex-col justify-center gap-8 py-8 lg:py-0">
          <div className="flex flex-col gap-3">
            <span className="inline-flex w-fit items-center rounded-full border border-[hsl(var(--blocks-primary-25))] bg-[hsl(var(--blocks-primary-shades-100))] px-3 py-1 text-xs font-medium text-primary">
              Selise Blocks
            </span>
            <h1 className="text-4xl font-bold leading-tight tracking-tight text-[hsl(var(--high-emphasis))] lg:text-5xl">
              Welcome to{" "}
              <span className="text-primary">Blocks OS</span>
            </h1>
            <p className="max-w-md text-base text-[hsl(var(--medium-emphasis))]">
              A unified platform to manage users, configure authentication flows, and secure your
              applications — all from one place.
            </p>
          </div>

          <div className="grid grid-cols-2 gap-3 sm:max-w-sm">
            {features.map(({ icon: Icon, label }) => (
              <div
                key={label}
                className="flex items-center gap-2 rounded-lg border border-[hsl(var(--border-default))] bg-white px-4 py-3 text-sm font-medium text-[hsl(var(--high-emphasis))] shadow-sm"
              >
                <Icon className="h-4 w-4 shrink-0 text-primary" />
                {label}
              </div>
            ))}
          </div>

          <div className="flex flex-col gap-2">
            <Button size="lg" className="w-fit px-10" disabled={isLoading} onClick={handleLogin}>
              Log in to your account
            </Button>
            {/* <p className="text-xs text-[hsl(var(--low-emphasis))]">
              Securely sign in using your credentials or SSO provider.
            </p> */}
          </div>
        </div>

        <BlockInfo />
      </div>
    </div>
  );
}

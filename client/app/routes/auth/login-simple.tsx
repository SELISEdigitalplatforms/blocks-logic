import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Logo } from "@/components/logo";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { ShieldCheck, Users, KeyRound, Puzzle, BookOpenText } from "lucide-react";
import { Link } from "react-router-dom";

const features = [
  { icon: ShieldCheck, label: "MFA & Passkeys" },
  { icon: Users, label: "User Management" },
  { icon: KeyRound, label: "OAuth 2.0 / OIDC" },
  { icon: Puzzle, label: "Modular Services" },
];

const ResourcesPanel = () => {
  const constructUrl = getRuntimeEnv("BLOCKS_CONSTRUCT_URL") || "https://construct.seliseblocks.com";

  return (
    <div className="mt-[24px] w-full lg:mt-0 lg:max-w-md">
      <div className="overflow-hidden rounded-2xl border border-[hsl(var(--border-default))] bg-white shadow-sm">
        {/* Header */}
        <div className="bg-gradient-to-br from-[hsl(var(--blocks-primary-shades-100))] to-[hsl(var(--blocks-primary-25))] px-6 py-5">
          <p className="text-[11px] font-semibold uppercase tracking-widest text-primary opacity-70">
            Developer Resources
          </p>
          <h3 className="mt-1 text-lg font-bold text-[hsl(var(--high-emphasis))]">
            Build with Blocks
          </h3>
          <p className="mt-1 text-sm text-[hsl(var(--medium-emphasis))]">
            Open-source SDKs and tools to accelerate your integration.
          </p>
          <Link
            to="https://docs.seliseblocks.com/"
            target="_blank"
            className="mt-3 inline-flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-xs font-medium text-white transition-colors hover:bg-primary/90"
          >
            <BookOpenText className="h-3 w-3" />
            Read the Docs
          </Link>
        </div>

        {/* Body */}
        <div className="divide-y divide-[hsl(var(--border-default))] px-6">
          {/* Frontend */}
          <div className="py-5">
            <p className="mb-3 text-[11px] font-semibold uppercase tracking-wider text-[hsl(var(--low-emphasis))]">
              Frontend
            </p>
            <div className="flex flex-col gap-3">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className="flex h-9 w-9 items-center justify-center rounded-lg border border-[hsl(var(--border-default))] bg-white shadow-sm">
                    <img src="/assets/images/react-icon.png" width={20} height={20} alt="React" />
                  </div>
                  <span className="text-sm font-medium text-[hsl(var(--high-emphasis))]">React</span>
                </div>
                <div className="flex items-center gap-3 text-xs">
                  <Link to="https://www.npmjs.com/package/@seliseblocks/cli" target="_blank" className="text-primary hover:underline">npm</Link>
                  <span className="h-3 w-px bg-[hsl(var(--border-default))]" />
                  <Link to="https://github.com/SELISEdigitalplatforms/l3-react-blocks-construct" target="_blank" className="text-primary hover:underline">GitHub</Link>
                  <span className="h-3 w-px bg-[hsl(var(--border-default))]" />
                  <Link to={constructUrl} target="_blank" className="text-primary hover:underline">Demo</Link>
                </div>
              </div>
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className="flex h-9 w-9 items-center justify-center rounded-lg border border-[hsl(var(--border-default))] bg-white shadow-sm opacity-40">
                    <img src="/assets/images/angular-icon.png" width={20} height={20} alt="Angular" />
                  </div>
                  <span className="text-sm font-medium text-[hsl(var(--medium-emphasis))]">Angular</span>
                </div>
                <span className="rounded-full bg-[hsl(var(--neutral-50))] px-2.5 py-0.5 text-xs text-[hsl(var(--low-emphasis))]">
                  Coming soon
                </span>
              </div>
            </div>
          </div>

          {/* Backend */}
          <div className="py-5">
            <p className="mb-3 text-[11px] font-semibold uppercase tracking-wider text-[hsl(var(--low-emphasis))]">
              Backend
            </p>
            <div className="flex flex-col gap-3">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className="flex h-9 w-9 items-center justify-center rounded-lg border border-[hsl(var(--border-default))] bg-white shadow-sm">
                    <img src="/assets/images/dotnet-icon.png" width={20} height={20} alt=".NET" />
                  </div>
                  <span className="text-sm font-medium text-[hsl(var(--high-emphasis))]">.NET</span>
                </div>
                <div className="flex items-center gap-3 text-xs">
                  <Link to="https://www.nuget.org/profiles/SELISE" target="_blank" className="text-primary hover:underline">NuGet</Link>
                  <span className="h-3 w-px bg-[hsl(var(--border-default))]" />
                  <Link to="https://github.com/SELISEdigitalplatforms/l0-net-blocks-construct" target="_blank" className="text-primary hover:underline">GitHub</Link>
                  <span className="h-3 w-px bg-[hsl(var(--border-default))]" />
                  <Link to="https://pypi.org/project/seliseblocks-lmt/" target="_blank" className="text-primary hover:underline">PyPI</Link>
                </div>
              </div>
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className="flex h-9 w-9 items-center justify-center rounded-lg border border-[hsl(var(--border-default))] bg-white shadow-sm opacity-40">
                    <img src="/assets/images/ruby-icon.png" width={20} height={20} alt="Ruby" />
                  </div>
                  <span className="text-sm font-medium text-[hsl(var(--medium-emphasis))]">Ruby</span>
                </div>
                <span className="rounded-full bg-[hsl(var(--neutral-50))] px-2.5 py-0.5 text-xs text-[hsl(var(--low-emphasis))]">
                  Coming soon
                </span>
              </div>
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="border-t border-[hsl(var(--border-default))] bg-[hsl(var(--surface-app))] px-6 py-4">
          <div className="flex items-center justify-between">
            <p className="text-sm text-[hsl(var(--medium-emphasis))]">Fully open source</p>
            <Link
              to="https://github.com/SELISEdigitalplatforms"
              target="_blank"
              className="inline-flex items-center gap-1.5 rounded-md border border-[hsl(var(--border-default))] bg-white px-3 py-1.5 text-xs font-medium text-[hsl(var(--high-emphasis))] shadow-sm transition-colors hover:bg-[hsl(var(--neutral-50))]"
            >
              <img src="/assets/images/social-media-github.png" width={14} height={14} alt="GitHub" />
              View on GitHub
            </Link>
          </div>
        </div>
      </div>
    </div>
  );
};

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
        `/dev-idp-proxy/api/Authentication/Authorize?${params.toString()}`,
        {
          method: "GET",
          headers: {
            accept: "*/*",
            "x-blocks-key": blocksKey,
            Authorization: "Basic c2VsaXNlYmxvY2tzOkJsMDNrc0B1JFU3VjEwUw==",
          },
        },
      );

      if (response.status === 302) {
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
            <Logo width={80} height={34} className="mb-1 opacity-90" />
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
          </div>
        </div>

        <ResourcesPanel />
      </div>
    </div>
  );
}

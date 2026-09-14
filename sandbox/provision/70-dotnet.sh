#!/usr/bin/env bash
# 70-dotnet.sh — .NET SDK 10 from the Ubuntu archive, for building Blocks.FunctionRunner
# on the VM. No third-party apt feed is needed on resolute.
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_facts; load_versions

need_root
[ "${PREFLIGHT_OK:-no}" = yes ] || die "run ./00-preflight.sh first"

step "Packages"
apt_install dotnet-sdk-10.0

step "Verification"
need_cmd dotnet
SDK="$(dotnet --version)"
fact DOTNET_SDK_VERSION "$SDK"
ok "dotnet SDK $SDK"
case "$SDK" in
  10.*) ok "SDK major version is 10" ;;
  *) die "expected a 10.x SDK, found $SDK" ;;
esac
DOTNET_RUNTIMES="$(dotnet --list-runtimes)"
grep -q 'Microsoft.NETCore.App 10\.' <<<"$DOTNET_RUNTIMES" || die "no .NET 10 runtime installed"
ok "$(grep -m1 'Microsoft.NETCore.App 10\.' <<<"$DOTNET_RUNTIMES")"

# Telemetry off and no first-run banner for a service build host.
write_file /etc/profile.d/dotnet-blocks.sh 0644 <<'PROFILE' || true
# Blocks Functions runner build host: quiet, offline-friendly dotnet defaults.
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
PROFILE

fact DOTNET_READY yes

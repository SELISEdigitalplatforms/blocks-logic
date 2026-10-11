#!/usr/bin/env bash
# 70-dotnet.sh — the .NET 10 SDK, for building Blocks.FunctionRunner on the VM.
#
# Three sources, tried in order, because no single one covers every supported release:
#   archive    the distro carries dotnet-sdk-10.0 (Ubuntu 26.04) - nothing third-party
#   microsoft  packages.microsoft.com (jammy, noble, bookworm, trixie)
#   tarball    dotnet-install.sh into /usr/local/dotnet, for releases no feed carries
#              (Ubuntu 20.04, Debian 10) - self-contained, and the one path apt security
#              updates never reach, so prefer a newer host where you can choose.
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_facts; load_versions

need_root
[ "${PREFLIGHT_OK:-no}" = yes ] || die "run ./00-preflight.sh first"

detect_os

DOTNET_INSTALL_DIR=/usr/local/dotnet
CACHE=/var/cache/blocks-fn

have_dotnet10() { command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; }

# Adds packages.microsoft.com. Returns non-zero rather than dying: a release the feed does
# not carry is not an error here, it is the reason the tarball path exists.
ensure_microsoft_feed() {
  if pkg_installed packages-microsoft-prod; then
    info "Microsoft package feed already configured"
    return 0
  fi
  local url="https://packages.microsoft.com/config/${OS_ID}/${OS_VERSION_ID}/packages-microsoft-prod.deb"
  local deb="$CACHE/packages-microsoft-prod-${OS_ID}-${OS_VERSION_ID}.deb"
  install -d -m 0755 "$CACHE"
  retry 3 5 -- curl -fsSL --connect-timeout 15 --max-time 300 -o "$deb.part" "$url" || return 1
  mv "$deb.part" "$deb"
  DEBIAN_FRONTEND=noninteractive dpkg -i "$deb" >/dev/null 2>&1 || return 1
  APT_UPDATED=0   # a new source makes every cached candidate stale
  ok "Microsoft package feed configured for $OS_ID $OS_VERSION_ID"
}

install_dotnet_tarball() {
  local script="$CACHE/dotnet-install.sh"
  install -d -m 0755 "$CACHE"
  retry 3 5 -- curl -fsSL --connect-timeout 15 --max-time 300 \
    -o "$script" https://dot.net/v1/dotnet-install.sh || die "could not download dotnet-install.sh"
  chmod 0755 "$script"
  # --no-path: PATH comes from the profile.d file below, not from a line this appends to
  # whatever shell rc it finds. Re-running is a no-op once the channel is current.
  "$script" --channel 10.0 --install-dir "$DOTNET_INSTALL_DIR" --no-path >/dev/null \
    || die "dotnet-install.sh failed"
  ln -sf "$DOTNET_INSTALL_DIR/dotnet" /usr/local/bin/dotnet
  # systemd services do not read /etc/profile.d, and the runner is published
  # framework-dependent — so without a registered install location the unit dies at startup
  # with "You must install .NET", no matter what PATH a login shell has. This file is the
  # documented lookup the apphost consults, and is exactly what the distro package writes.
  install -d -m 0755 /etc/dotnet
  printf '%s\n' "$DOTNET_INSTALL_DIR" | write_file /etc/dotnet/install_location 0644 || true
  ok "installed the .NET 10 SDK under $DOTNET_INSTALL_DIR (registered in /etc/dotnet/install_location)"
}

step "Packages"
if have_dotnet10; then
  DOTNET_SOURCE=present
  info "a 10.x SDK is already installed - nothing to do"
elif pkg_available dotnet-sdk-10.0; then
  DOTNET_SOURCE=archive
  pkg_install dotnet-sdk-10.0
elif ensure_microsoft_feed && pkg_available dotnet-sdk-10.0; then
  DOTNET_SOURCE=microsoft
  pkg_install dotnet-sdk-10.0
else
  DOTNET_SOURCE=tarball
  warn "no dotnet-sdk-10.0 package for $OS_ID $OS_VERSION_ID - falling back to dotnet-install.sh"
  install_dotnet_tarball
fi
fact DOTNET_SOURCE "$DOTNET_SOURCE"
ok "dotnet from: $DOTNET_SOURCE"

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
# The runner's runtimeconfig.json asks for both shared frameworks; a host with only the base
# runtime starts nothing, and says so in a way that names neither this script nor the SDK.
grep -q 'Microsoft.AspNetCore.App 10\.' <<<"$DOTNET_RUNTIMES" \
  || die "no ASP.NET Core 10 shared framework - the runner is published framework-dependent against it"
ok "$(grep -m1 'Microsoft.AspNetCore.App 10\.' <<<"$DOTNET_RUNTIMES")"

# Telemetry off and no first-run banner for a service build host.
# DOTNET_ROOT matters only for the tarball install, where nothing else records where the
# SDK lives; it is harmless when the package owns /usr/lib/dotnet.
write_file /etc/profile.d/dotnet-blocks.sh 0644 <<PROFILE || true
# Blocks Functions runner build host: quiet, offline-friendly dotnet defaults.
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
$([ "$DOTNET_SOURCE" = tarball ] && printf 'export DOTNET_ROOT=%s\nexport PATH="$DOTNET_ROOT:$PATH"' "$DOTNET_INSTALL_DIR")
PROFILE

fact DOTNET_READY yes

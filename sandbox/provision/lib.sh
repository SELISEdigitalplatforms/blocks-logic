#!/usr/bin/env bash
# lib.sh — shared helpers for the runner VM provisioning scripts.
# Sourced, never executed. Every script that sources it must set `set -euo pipefail`.

# shellcheck disable=SC2034
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SANDBOX_ROOT="$(cd "$LIB_DIR/.." && pwd)"
FACTS_FILE="${FACTS_FILE:-$LIB_DIR/.facts}"
VERSIONS_FILE="${VERSIONS_FILE:-$LIB_DIR/.versions}"
PROVISION_LOG="${PROVISION_LOG:-$SANDBOX_ROOT/provision.log}"

# ---------------------------------------------------------------- output ----
if [ -t 1 ] && [ -z "${NO_COLOR:-}" ]; then
  C_OK=$'\033[32m'; C_WARN=$'\033[33m'; C_ERR=$'\033[31m'; C_DIM=$'\033[2m'; C_OFF=$'\033[0m'
else
  C_OK=""; C_WARN=""; C_ERR=""; C_DIM=""; C_OFF=""
fi

_stamp() { date -u +%FT%TZ; }
_logfile() { printf '%s %s\n' "$(_stamp)" "$*" >>"$PROVISION_LOG" 2>/dev/null || true; }

step() { printf '\n%s==> %s%s\n' "$C_DIM" "$*" "$C_OFF"; _logfile "STEP $*"; }
ok()   { printf '%s  ok%s   %s\n'   "$C_OK"   "$C_OFF" "$*"; _logfile "OK $*"; }
info() { printf '  ..   %s\n' "$*"; _logfile "INFO $*"; }
warn() { printf '%s  warn%s %s\n' "$C_WARN" "$C_OFF" "$*" >&2; _logfile "WARN $*"; }
die()  { printf '%s  fail%s %s\n' "$C_ERR"  "$C_OFF" "$*" >&2; _logfile "FAIL $*"; exit 1; }

# ------------------------------------------------------------- guardrails ----
need_root() { [ "$(id -u)" = 0 ] || die "must run as root (use sudo)"; }
need_cmd()  { command -v "$1" >/dev/null 2>&1 || die "required command not found: $1"; }

# retry <attempts> <sleep-seconds> -- <command...>
retry() {
  local attempts="$1" delay="$2"; shift 2; [ "${1:-}" = "--" ] && shift
  local n=1
  until "$@"; do
    if [ "$n" -ge "$attempts" ]; then return 1; fi
    warn "attempt $n/$attempts failed: $* (retrying in ${delay}s)"
    sleep "$delay"; n=$((n + 1))
  done
}

# ------------------------------------------------------------ idempotency ----
# write_file <path> <mode> [owner]  — content on stdin; only writes when changed.
write_file() {
  local path="$1" mode="$2" owner="${3:-}" tmp
  tmp="$(mktemp "${TMPDIR:-/tmp}/provision.XXXXXX")"
  cat >"$tmp"
  [ -d "$(dirname "$path")" ] || install -d -m 0755 "$(dirname "$path")"
  if [ -f "$path" ] && cmp -s "$tmp" "$path"; then
    chmod "$mode" "$path"; [ -n "$owner" ] && chown "$owner" "$path"
    rm -f "$tmp"; info "unchanged: $path"; return 1
  fi
  [ -f "$path" ] && cp -a "$path" "$path.bak.$(date -u +%Y%m%d%H%M%S)"
  install -m "$mode" ${owner:+-o "${owner%%:*}" -g "${owner##*:}"} "$tmp" "$path"
  rm -f "$tmp"; ok "wrote: $path"; return 0
}

# fact <key> [value] — read or record a host fact. Values are single-quoted so they may
# contain spaces and still survive being sourced by load_facts().
fact() {
  local key="$1"
  if [ $# -ge 2 ]; then
    local val esc tmp
    val="$2"
    esc="${val//\'/\'\\\'\'}"                       # ' -> '\''
    touch "$FACTS_FILE"
    tmp="$(mktemp "${TMPDIR:-/tmp}/facts.XXXXXX")"
    grep -v "^${key}=" "$FACTS_FILE" >"$tmp" 2>/dev/null || true
    printf "%s='%s'\n" "$key" "$esc" >>"$tmp"
    cat "$tmp" >"$FACTS_FILE"; rm -f "$tmp"
  else
    sed -n "s|^${key}=||p" "$FACTS_FILE" 2>/dev/null | tail -1 | sed "s/^'//; s/'\$//"
  fi
}

load_facts()    { [ -f "$FACTS_FILE" ] && set -a && . "$FACTS_FILE" && set +a || true; }
load_versions() { [ -f "$VERSIONS_FILE" ] && set -a && . "$VERSIONS_FILE" && set +a || true; }

# ---------------------------------------------------------------- os ----
# One place knows what this host is; everything that installs goes through pkg_install,
# so teaching the provisioning a new family means adding a case here and nowhere else.
#
# Supported: Ubuntu 20.04+ and Debian 10+. Everything this repo installs has a path on
# all of them — Docker comes from download.docker.com when the distro's Engine is older
# than 20.10, and .NET 10 from dotnet-install.sh when no package exists. What the older
# two cannot supply is a kernel >= 5.10 (gVisor's systrap platform) and a unified cgroup
# v2 hierarchy: Ubuntu switched to cgroup v2 in 21.10 and Debian in 11, and both take a
# reboot to change. 00-preflight.sh checks those two and prints how to fix them.
detect_os() {
  [ -r /etc/os-release ] || die "/etc/os-release is missing - cannot identify this host"
  # shellcheck disable=SC1091
  . /etc/os-release
  OS_ID="${ID:-unknown}"
  OS_VERSION_ID="${VERSION_ID:-0}"
  OS_CODENAME="${VERSION_CODENAME:-unknown}"
  case "$OS_ID" in
    ubuntu|debian) OS_FAMILY=debian ;;
    *) case " ${ID_LIKE:-} " in *" debian "*|*" ubuntu "*) OS_FAMILY=debian ;; *) OS_FAMILY="$OS_ID" ;; esac ;;
  esac
  if command -v apt-get >/dev/null 2>&1; then PKG_MANAGER=apt; else PKG_MANAGER=""; fi
  export OS_ID OS_VERSION_ID OS_CODENAME OS_FAMILY PKG_MANAGER
}

# version_ge <have> <want> — dotted numeric comparison ("22.04" >= "22.04", "12" >= "11").
version_ge() { [ "$(printf '%s\n%s\n' "$2" "$1" | sort -V | head -1)" = "$2" ]; }

# os_supported — is this a release the provisioning actually knows how to build?
os_supported() {
  case "${OS_ID:-}" in
    ubuntu) version_ge "$OS_VERSION_ID" 20.04 ;;
    debian) version_ge "$OS_VERSION_ID" 10 ;;
    *) return 1 ;;
  esac
}

# ----------------------------------------------------------- packages ----
# pkg_install <pkg...>   install what is missing, and nothing else
# pkg_installed <pkg>    is it installed?
# pkg_available <pkg>    does this host have an installation candidate for it?
#
# All three are idempotent and quiet on a second run: that is what makes a redeploy free.
APT_UPDATED=0
_apt_refresh() {
  [ "$APT_UPDATED" = 0 ] || return 0
  info "apt-get update"
  DEBIAN_FRONTEND=noninteractive retry 3 5 -- apt-get update -qq || die "apt-get update failed"
  APT_UPDATED=1
}

pkg_installed() {
  case "${PKG_MANAGER:-}" in
    apt) dpkg -s "$1" >/dev/null 2>&1 ;;
    *) return 1 ;;
  esac
}

pkg_available() {
  case "${PKG_MANAGER:-}" in
    apt)
      _apt_refresh >&2
      apt-cache policy "$1" 2>/dev/null | sed -n 's/^  Candidate: //p' | grep -qv '(none)'
      ;;
    *) return 1 ;;
  esac
}

pkg_candidate() {
  case "${PKG_MANAGER:-}" in
    apt)
      _apt_refresh >&2
      apt-cache policy "$1" 2>/dev/null | sed -n 's/^  Candidate: //p' | grep -v '(none)' | head -1
      ;;
    *) return 1 ;;
  esac
}

pkg_install() {
  case "${PKG_MANAGER:-}" in
    apt) _pkg_install_apt "$@" ;;
    "")  die "no supported package manager on this host (apt-get not found) - cannot install: $*" ;;
    *)   die "unsupported package manager '${PKG_MANAGER}' - cannot install: $*" ;;
  esac
}

_pkg_install_apt() {
  local missing=() p
  for p in "$@"; do dpkg -s "$p" >/dev/null 2>&1 || missing+=("$p"); done
  if [ ${#missing[@]} -eq 0 ]; then info "already installed: $*"; return 0; fi
  _apt_refresh
  info "installing: ${missing[*]}"
  DEBIAN_FRONTEND=noninteractive retry 3 5 -- \
    apt-get install -y -qq --no-install-recommends "${missing[@]}" >/dev/null \
    || die "apt-get install failed: ${missing[*]}"
  ok "installed: ${missing[*]}"
}

# Kept so older call sites and muscle memory keep working; pkg_install is the entry point.
apt_install() { pkg_install "$@"; }

# BASE_TOOLS — what the provisioning itself runs on, installed before anything else.
# `make` is in this list because deploy.sh drives every phase through it and a minimal
# cloud image has none: without this, the first command on a fresh host fails with
# "make: command not found" before preflight can explain anything. `nftables` is here for
# the same reason in reverse — preflight *requires* nft and nothing used to install it.
BASE_TOOLS_DEBIAN="make curl ca-certificates tar zstd jq nftables rsync gnupg"

ensure_base_tools() {
  detect_os
  case "$OS_FAMILY" in
    debian)
      # shellcheck disable=SC2086
      pkg_install $BASE_TOOLS_DEBIAN
      ;;
    *)
      die "unsupported OS family '${OS_FAMILY}' (${OS_ID} ${OS_VERSION_ID}) - this provisioning installs with apt"
      ;;
  esac
}

# download_verified <url> <dest> <sha512>
download_verified() {
  local url="$1" dest="$2" want="$3" got
  if [ -f "$dest" ]; then
    got="$(sha512sum "$dest" | awk '{print $1}')"
    [ "$got" = "$want" ] && { info "cached and verified: $dest"; return 0; }
    warn "checksum mismatch on cached $dest — re-downloading"
  fi
  install -d -m 0755 "$(dirname "$dest")"
  retry 3 5 -- curl -fsSL --connect-timeout 15 --max-time 900 -o "$dest.part" "$url" \
    || die "download failed: $url"
  got="$(sha512sum "$dest.part" | awk '{print $1}')"
  [ "$got" = "$want" ] || { rm -f "$dest.part"; die "sha512 mismatch for $url
  expected $want
  got      $got"; }
  mv "$dest.part" "$dest"; ok "downloaded and verified: $dest"
}

# ---------------------------------------------------------- build cache ----
# BuildKit's cache is the one thing on this host that grows without an owner: ImageGc prunes
# function images and the registry, nothing prunes build layers. Express the ceiling as a
# share of the disk rather than a fixed size, so the same number is right on a 20 GB VM and a
# 500 GB one. Floored at 512 MB, because a cache smaller than a single image build is just a
# slow build with extra steps.
build_cache_max_mb() {   # [percent]
  local pct="${1:-2}" target=/var/lib/docker total_kb mb
  [ -d "$target" ] || target=/var
  total_kb="$(df -Pk "$target" | awk 'NR==2{print $2}')"
  mb=$(( total_kb / 1024 * pct / 100 ))
  [ "$mb" -ge 512 ] || mb=512
  printf '%s' "$mb"
}

# ------------------------------------------------------- runner downtime ----
# blocks-function-runner BindsTo blocks-fn-firewall.service, which is PartOf docker.service.
# Restarting either therefore stops the runner, and systemd does not bring it back: without
# these two the host stays runner-less from phase 1 until phase 5 installs — including on a
# deploy that then fails the configuration gate. Note what they do NOT do: they never start a
# runner that was not already running, so a fresh host still comes up only in phase 5.
runner_active() { systemctl is-active --quiet blocks-function-runner.service; }

runner_resume() {   # <was-active:yes|no>
  [ "${1:-no}" = yes ] || return 0
  runner_active && return 0
  info "blocks-function-runner was stopped by the restart above - starting it again"
  systemctl start blocks-function-runner.service \
    || warn "could not start blocks-function-runner.service - phase 5 installs and starts it"
}

# systemd_enable <unit>
systemd_enable() {
  systemctl enable --now "$1" >/dev/null 2>&1 || die "could not enable $1"
  systemctl is-active --quiet "$1" || die "$1 is not active"
  ok "$1 active and enabled"
}

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

# apt_install <pkg...> — quiet, non-interactive, only installs what is missing.
APT_UPDATED=0
apt_install() {
  local missing=()
  for p in "$@"; do dpkg -s "$p" >/dev/null 2>&1 || missing+=("$p"); done
  if [ ${#missing[@]} -eq 0 ]; then info "already installed: $*"; return 0; fi
  if [ "$APT_UPDATED" = 0 ]; then
    info "apt-get update"
    DEBIAN_FRONTEND=noninteractive retry 3 5 -- apt-get update -qq || die "apt-get update failed"
    APT_UPDATED=1
  fi
  info "installing: ${missing[*]}"
  DEBIAN_FRONTEND=noninteractive retry 3 5 -- \
    apt-get install -y -qq --no-install-recommends "${missing[@]}" >/dev/null || die "apt-get install failed: ${missing[*]}"
  ok "installed: ${missing[*]}"
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

# systemd_enable <unit>
systemd_enable() {
  systemctl enable --now "$1" >/dev/null 2>&1 || die "could not enable $1"
  systemctl is-active --quiet "$1" || die "$1 is not active"
  ok "$1 active and enabled"
}

#!/usr/bin/env bash
# 00-preflight.sh — read-only inspection of the host. Changes nothing.
# Records everything the later scripts branch on into provision/.facts and refuses to
# pass if a hard requirement is missing. Must succeed before anything else runs.
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_versions

FAILURES=0
require() { # require <description> <condition-result 0/1>
  if [ "$2" = 0 ]; then ok "$1"; else printf '%s  fail%s %s\n' "$C_ERR" "$C_OFF" "$1" >&2; _logfile "FAIL $1"; FAILURES=$((FAILURES + 1)); fi
}

need_root
: >"$FACTS_FILE"
fact PREFLIGHT_AT "$(_stamp)"

# ------------------------------------------------------------------ system ----
step "System"
. /etc/os-release
fact OS_ID "$ID"; fact OS_VERSION "$VERSION_ID"; fact OS_CODENAME "${VERSION_CODENAME:-unknown}"
fact KERNEL "$(uname -r)"; fact ARCH "$(uname -m)"
info "$ID $VERSION_ID (${VERSION_CODENAME:-?}) kernel $(uname -r) $(uname -m)"
require "architecture is x86_64" "$([ "$(uname -m)" = x86_64 ] && echo 0 || echo 1)"

# Every later script installs with apt and names Ubuntu's packages, and 70-dotnet.sh takes
# dotnet-sdk-10.0 straight from the distro archive — which no release before 26.04 carries. A
# host that is not this is not a supported host, and finding that out half way through phase 1
# (after Docker and gVisor are already installed) is the worst time to find it out.
# FN_ALLOW_UNTESTED_OS=1 downgrades both checks to warnings for an operator who has read this.
os_ok=0
[ "$ID" = ubuntu ] || os_ok=1
awk -v v="${VERSION_ID:-0}" 'BEGIN{split(v,p,"."); exit !((p[1]+0)>26 || ((p[1]+0)==26 && (p[2]+0)>=4))}' || os_ok=1
if [ "${FN_ALLOW_UNTESTED_OS:-0}" = 1 ]; then
  [ "$os_ok" = 0 ] || warn "FN_ALLOW_UNTESTED_OS=1 — continuing on $ID ${VERSION_ID:-?}, which is not Ubuntu 26.04 or newer"
  ok "operating system check overridden"
else
  require "Ubuntu 26.04 or newer (dotnet-sdk-10.0 comes from the distro archive; set FN_ALLOW_UNTESTED_OS=1 to override)" "$os_ok"
fi
require "kernel >= 5.10 (gVisor systrap)" \
  "$(awk 'BEGIN{split(ARGV[1],v,"."); exit !(v[1]>5 || (v[1]==5 && v[2]>=10))}' "$(uname -r)" && echo 0 || echo 1)"

CPUS="$(nproc)"; MEM_MB="$(awk '/MemTotal/{printf "%d", $2/1024}' /proc/meminfo)"
DISK_MB="$(df -Pm /var | awk 'NR==2{print $4}')"
fact CPUS "$CPUS"; fact MEMORY_MB "$MEM_MB"; fact DISK_FREE_MB "$DISK_MB"
info "cpus=$CPUS memory=${MEM_MB}MB free-disk(/var)=${DISK_MB}MB"
require "at least 2 cpus" "$([ "$CPUS" -ge 2 ] && echo 0 || echo 1)"
require "at least 4096 MB memory" "$([ "$MEM_MB" -ge 4096 ] && echo 0 || echo 1)"
require "at least 20480 MB free on /var" "$([ "$DISK_MB" -ge 20480 ] && echo 0 || echo 1)"

# ----------------------------------------------------------------- cgroups ----
step "cgroup v2"
CG_TYPE="$(stat -fc %T /sys/fs/cgroup 2>/dev/null || echo none)"
fact CGROUP_TYPE "$CG_TYPE"
require "unified cgroup v2 hierarchy at /sys/fs/cgroup" "$([ "$CG_TYPE" = cgroup2fs ] && echo 0 || echo 1)"
CG_CONTROLLERS="$(cat /sys/fs/cgroup/cgroup.controllers 2>/dev/null || true)"
fact CGROUP_CONTROLLERS "$CG_CONTROLLERS"
for c in cpu memory pids cpuset; do
  case " $CG_CONTROLLERS " in *" $c "*) require "cgroup controller: $c" 0 ;; *) require "cgroup controller: $c" 1 ;; esac
done

# ---------------------------------------------------------- gVisor platform ----
step "gVisor platform"
if [ -e /dev/kvm ] && [ -r /dev/kvm ] && [ -w /dev/kvm ]; then
  GVISOR_PLATFORM=kvm; info "/dev/kvm present and usable"
else
  GVISOR_PLATFORM=systrap; info "no usable /dev/kvm — systrap platform"
fi
fact GVISOR_PLATFORM "$GVISOR_PLATFORM"
fact GVISOR_RELEASE "${GVISOR_RELEASE:-unset}"
require "gVisor release pinned in .versions" "$([ -n "${GVISOR_RELEASE:-}" ] && [ -n "${GVISOR_BUNDLE_SHA512:-}" ] && echo 0 || echo 1)"
fact SECCOMP_FILTER "$(grep -q 'Seccomp' /proc/self/status && echo yes || echo unknown)"

# ---------------------------------------------------------------- network ----
step "Network"
PRIMARY_IF="$(ip -4 route show default | awk '/default/{print $5; exit}')"
PRIMARY_IP="$(ip -4 -o addr show dev "$PRIMARY_IF" scope global | awk '{print $4; exit}')"
fact PRIMARY_IF "$PRIMARY_IF"
fact PRIMARY_CIDR "$PRIMARY_IP"
fact PRIMARY_IPV4 "${PRIMARY_IP%%/*}"
info "primary interface $PRIMARY_IF ${PRIMARY_IP}"
require "primary interface resolved" "$([ -n "$PRIMARY_IF" ] && [ -n "$PRIMARY_IP" ] && echo 0 || echo 1)"

# Captured before matching, not piped into grep -q: grep exits at the first match and
# the producer then dies of SIGPIPE, which `set -o pipefail` turns into a failed check.
# Every `grep -q` in this repo is written this way for that reason.
IP6_OUT="$(ip -6 addr show scope global 2>/dev/null || true)"
if grep -q inet6 <<<"$IP6_OUT"; then IPV6=yes; else IPV6=no; fi
fact IPV6_GLOBAL "$IPV6"; info "global IPv6 addresses: $IPV6"
fact IP_FORWARD "$(sysctl -n net.ipv4.ip_forward 2>/dev/null || echo unknown)"

for c in nft iptables ufw; do
  if command -v "$c" >/dev/null 2>&1; then fact "HAS_${c^^}" yes; else fact "HAS_${c^^}" no; fi
done
require "nft present (own nftables table)" "$(command -v nft >/dev/null 2>&1 && echo 0 || echo 1)"
UFW_OUT="$(command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null || true)"
if grep -qi '^Status: active' <<<"$UFW_OUT"; then UFW=active; else UFW=inactive; fi
fact UFW_STATE "$UFW"; info "ufw: $UFW"
[ "$UFW" = active ] && warn "ufw is active — its rules sit at filter priority 0; the blocks_fn table at filter-10 still applies, but review interaction"
IPT_BACKEND="$(iptables -V 2>/dev/null | sed -n 's/.*(\(.*\)).*/\1/p')"
fact IPTABLES_BACKEND "${IPT_BACKEND:-unknown}"; info "iptables backend: ${IPT_BACKEND:-unknown}"

# --------------------------------------------------------------- commands ----
step "Required commands"
for c in bash awk sed grep curl tar install systemctl id getent sha512sum; do
  if command -v "$c" >/dev/null 2>&1; then ok "command: $c"; else require "command: $c" 1; fi
done
for c in zstd jq; do
  command -v "$c" >/dev/null 2>&1 && ok "command: $c (present)" || info "command: $c missing — installed by a later script"
done

# ------------------------------------------------- existing runtime pieces ----
step "Existing runtime components"
comp() { # comp <name> <command> [version-args...]
  local name="$1" cmd="$2"; shift 2
  if command -v "$cmd" >/dev/null 2>&1; then
    # No pipe into head: under `set -o pipefail` a producer that is still writing when
    # head exits takes SIGPIPE and the pipeline returns 141, which killed preflight at
    # random on multi-line output like `runsc --version`. Capture, then take line one.
    local v out; out="$("$cmd" "$@" 2>/dev/null || true)"; v="${out%%$'\n'*}"
    fact "HAS_${name}" yes; fact "${name}_VERSION" "$v"; info "$cmd present: $v"
  else
    fact "HAS_${name}" no; info "$cmd not installed"
  fi
}
comp DOCKER docker --version
comp CONTAINERD containerd --version
comp RUNSC runsc --version
comp DOTNET dotnet --version
comp NODE node --version
fact HAS_DOCKER_SERVICE "$(systemctl list-unit-files docker.service >/dev/null 2>&1 && systemctl is-enabled docker.service 2>/dev/null || echo absent)"
fact HAS_RUNNER_USER "$(getent passwd blocks-runner >/dev/null && echo yes || echo no)"
fact HAS_FN_NETWORK "$(docker network inspect blocks-fn-egress >/dev/null 2>&1 && echo yes || echo no)"

# ------------------------------------------------------------ reachability ----
step "Reachability"
reach() { # reach <label> <url> <fact>
  local code
  code="$(curl -sS -o /dev/null -w '%{http_code}' --connect-timeout 10 --max-time 25 "$2" 2>/dev/null || echo 000)"
  if [ "$code" != 000 ]; then ok "$1 reachable (HTTP $code)"; fact "$3" "ok:$code"; else require "$1 reachable" 1; fact "$3" unreachable; fi
}
UBUNTU_HOSTS="$(awk '/^(deb|URIs:)/{for(i=1;i<=NF;i++) if ($i ~ /^https?:\/\//) {print $i; exit}}' \
  /etc/apt/sources.list.d/*.sources /etc/apt/sources.list 2>/dev/null || true)"
UBUNTU_HOST="${UBUNTU_HOSTS%%$'\n'*}"
reach "ubuntu archive" "${UBUNTU_HOST:-http://archive.ubuntu.com/ubuntu/}" REACH_UBUNTU
reach "gvisor storage"  "${GVISOR_BASE_URL}/${GVISOR_RELEASE}/${GVISOR_ARCH}/${GVISOR_BUNDLE}.sha512" REACH_GVISOR
reach "docker hub"      "https://registry-1.docker.io/v2/" REACH_DOCKERHUB
reach "npm registry"    "https://registry.npmjs.org/" REACH_NPM
reach "nuget"           "https://api.nuget.org/v3/index.json" REACH_NUGET

# ------------------------------------------------------- the Blocks endpoints ----
# The runner's whole job is to reach the Blocks Redis, and on this network that means the VPN
# is up. Nothing here used to check it, so a VM without the VPN passed all five earlier phases
# and failed at the very end of deploy.sh on a heartbeat — after provisioning, building and
# installing. Checking it first is the difference between a two-minute fix and a twenty-minute
# one. Set FN_SKIP_ENDPOINT_PROBE=1 to go ahead anyway (reconciling a host during an outage).
step "Blocks endpoints"

# A plain TCP connect. bash's /dev/tcp rather than nc, which is not installed on a minimal
# cloud image and would make this check depend on a package this script has not installed yet.
tcp_open() { # tcp_open <host> <port>
  timeout 5 bash -c "exec 3<>/dev/tcp/$1/$2" 2>/dev/null
}

RUNNER_ENV=/etc/blocks-runner/runner.env
env_line() { # env_line <key> — a value from runner.env, or empty
  [ -f "$RUNNER_ENV" ] || return 0
  sed -n "s/^[[:space:]]*$1=//p" "$RUNNER_ENV" 2>/dev/null | tail -1 | tr -d '"'"'"'' | tr -d '[:space:]'
}

# Explicit beats discovered: a Key Vault host keeps nothing on disk to find, so this is the
# only way to have the check run there at all.
PROBE_TARGET="${FN_ENDPOINT_PROBE:-}"
PROBE_LABEL="FN_ENDPOINT_PROBE"

if [ -z "$PROBE_TARGET" ] && [ -f "$RUNNER_ENV" ]; then
  case "$(env_line BLOCKS_VAULT_TYPE)" in
    1)
      # StackExchange form: host:port,password=…,ssl=True — take the first element.
      CACHE="$(env_line BlocksSecret__CacheConnectionString)"
      PROBE_TARGET="${CACHE%%,*}"
      PROBE_LABEL="Blocks Redis (BlocksSecret__CacheConnectionString)"
      ;;
    2)
      # Nothing on disk names Redis here; the vault is what must be reachable to find it, and
      # it sits on the same private network, so it stands in for the same question.
      VAULT_URL="$(env_line KeyVault__KeyVaultUrl)"
      if [ -n "$VAULT_URL" ]; then
        VAULT_HOST="${VAULT_URL#*://}"; VAULT_HOST="${VAULT_HOST%%/*}"
        PROBE_TARGET="${VAULT_HOST%%:*}:443"
        PROBE_LABEL="Azure Key Vault (KeyVault__KeyVaultUrl)"
      fi
      ;;
  esac
fi

fact ENDPOINT_PROBE_TARGET "${PROBE_TARGET:-none}"
if [ "${FN_SKIP_ENDPOINT_PROBE:-0}" = 1 ]; then
  warn "FN_SKIP_ENDPOINT_PROBE=1 — not checking that this host can reach the Blocks endpoints"
  fact ENDPOINT_PROBE skipped
elif [ -z "$PROBE_TARGET" ]; then
  # A fresh host has no runner.env yet, so this is the normal first-run outcome, not a fault.
  info "no endpoint to probe yet — set FN_ENDPOINT_PROBE=host:port to check the VPN from here"
  fact ENDPOINT_PROBE unknown
else
  PROBE_HOST="${PROBE_TARGET%:*}"; PROBE_PORT="${PROBE_TARGET##*:}"
  if [ "$PROBE_HOST" = "$PROBE_PORT" ] || [ -z "$PROBE_PORT" ]; then
    warn "cannot read a host:port out of '$PROBE_TARGET' — not probing"
    fact ENDPOINT_PROBE unreadable
  elif tcp_open "$PROBE_HOST" "$PROBE_PORT"; then
    ok "$PROBE_LABEL reachable at $PROBE_HOST:$PROBE_PORT"
    fact ENDPOINT_PROBE "ok:$PROBE_HOST:$PROBE_PORT"
  else
    fact ENDPOINT_PROBE "unreachable:$PROBE_HOST:$PROBE_PORT"
    require "$PROBE_LABEL reachable at $PROBE_HOST:$PROBE_PORT — is the VPN up? (FN_SKIP_ENDPOINT_PROBE=1 to continue anyway)" 1
  fi
fi

# ------------------------------------------------------------------ verdict ----
step "Verdict"
fact PREFLIGHT_FAILURES "$FAILURES"
if [ "$FAILURES" -gt 0 ]; then
  fact PREFLIGHT_OK no
  die "$FAILURES preflight check(s) failed — nothing else may run"
fi
fact PREFLIGHT_OK yes
ok "preflight passed — facts written to $FACTS_FILE"

#!/usr/bin/env bash
# deploy.sh — bare VM to a running Blocks Functions runner, in one command.
#
#   sudo ./deploy.sh                 provision, build, test, install, verify
#   sudo ./deploy.sh --skip-tests    same, without the test suites (faster redeploys)
#   sudo ./deploy.sh --help
#
# Safe to re-run: every phase is idempotent, so this is also the way to reconcile a host
# after editing deny-cidrs, dns-servers, logging.conf or .versions. The service is only
# restarted in phase 5, after the configuration gate and the tests have passed, so a bad
# build or a half-filled runner.env never takes down a runner that is currently working.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$HERE/provision/lib.sh"

SKIP_TESTS=no
while [ $# -gt 0 ]; do
  case "$1" in
    --skip-tests) SKIP_TESTS=yes ;;
    -h|--help) sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) die "unknown option '$1' (try --help)" ;;
  esac
  shift
done

need_root
PHASES=6
phase() { printf '\n\033[1m[%d/%d] %s\033[0m\n' "$1" "$PHASES" "$2"; _logfile "PHASE $1/$PHASES $2"; }

START="$(date +%s)"

# ---------------------------------------------------------------- 1. host ----
phase 1 "Host provisioning"
# preflight, docker, gVisor, egress network + firewall, service user, registry, dotnet,
# log retention. Each script re-checks its own work and refuses to continue on failure.
make -s -C "$HERE/provision" all
# Facts are written by the scripts above, so read them only now.
load_facts

# --------------------------------------------------------------- 2. image ----
phase 2 "Runtime image"
"$HERE/runtime-image/build.sh"

# -------------------------------------------------------------- 3. config ----
# The one thing this repo cannot supply. A runner that starts without real credentials
# does not fail loudly — it sits in a retry loop looking healthy — so the gate is here,
# before anything is installed, rather than in the logs afterwards.
phase 3 "Configuration"
ENV_FILE=/etc/blocks-runner/runner.env
[ -f "$ENV_FILE" ] || die "$ENV_FILE is missing — run provision/40-runner-user.sh to seed it, then fill it in"

env_val() { sed -n "s/^[[:space:]]*$1=//p" "$ENV_FILE" | tail -1 | tr -d '"'"'"'' | tr -d '[:space:]'; }
require_val() {
  local key val; key="$1"; val="$(env_val "$key")"
  [ -n "$val" ] || die "$ENV_FILE: $key is empty — the runner cannot start without it"
  # The shipped example uses U+2026 as the "put your value here" marker.
  case "$val" in *…*|*"put-your"*|*CHANGEME*) die "$ENV_FILE: $key still holds the example placeholder" ;; esac
}

VAULT="$(env_val BLOCKS_VAULT_TYPE)"
case "$VAULT" in
  1)
    info "vault: OnPrem — secrets read from $ENV_FILE"
    require_val BlocksSecret__CacheConnectionString
    require_val BlocksSecret__DatabaseConnectionString
    ;;
  2)
    info "vault: Azure Key Vault"
    for k in KeyVault__ClientId KeyVault__ClientSecret KeyVault__KeyVaultUrl KeyVault__TenantId; do
      require_val "$k"
    done
    ;;
  *) die "$ENV_FILE: BLOCKS_VAULT_TYPE is '${VAULT:-unset}', expected 1 (OnPrem) or 2 (Key Vault)" ;;
esac
require_val RUNNER__RunnerId
ok "runner id: $(env_val RUNNER__RunnerId)"

# The runtime is not a tuning knob. The runner refuses to claim work when it is set to
# anything but runsc, so a host deployed this way would come up healthy-looking and idle;
# failing here says why, before the unit is restarted rather than after.
CONFIGURED_RUNTIME="$(env_val RUNNER__Runtime)"
case "$CONFIGURED_RUNTIME" in
  ""|runsc) ok "sandbox runtime: runsc" ;;
  *) die "$ENV_FILE: RUNNER__Runtime is '$CONFIGURED_RUNTIME', not runsc — tenant code runs under gVisor or it does not run" ;;
esac

# Credentials must not be world-readable on a host that executes untrusted code.
PERMS="$(stat -c '%U:%G %a' "$ENV_FILE")"
[ "$PERMS" = "root:blocks-runner 640" ] || die "$ENV_FILE is $PERMS, expected root:blocks-runner 640"
ok "$ENV_FILE validated ($PERMS)"

# ---------------------------------------------------------------- 4. test ----
phase 4 "Build and test"
if [ "$SKIP_TESTS" = yes ]; then
  warn "--skip-tests: the .NET and runtime suites did not run"
  make -s -C "$HERE" build
else
  make -s -C "$HERE" test
fi

# ------------------------------------------------------------- 5. install ----
phase 5 "Install"
WAS_ACTIVE=no
systemctl is-active --quiet blocks-function-runner.service && WAS_ACTIVE=yes
make -s -C "$HERE" install

# -------------------------------------------------------------- 6. verify ----
phase 6 "Verification"
UNIT=blocks-function-runner.service

# Type=notify: systemd reports active only once the host has signalled readiness, so a
# unit that is still active a few seconds later has actually started, not just forked.
for _ in $(seq 1 30); do
  systemctl is-active --quiet "$UNIT" && break
  sleep 1
done
systemctl is-active --quiet "$UNIT" || {
  journalctl -u "$UNIT" -n 30 --no-pager || true
  die "$UNIT did not come up — journal above"
}
PID1="$(systemctl show -p MainPID --value "$UNIT")"
sleep 5
PID2="$(systemctl show -p MainPID --value "$UNIT")"
[ "$PID1" = "$PID2" ] || die "$UNIT is restarting (pid $PID1 -> $PID2) — it is crash-looping"
ok "$UNIT active and stable (pid $PID2)"

systemctl is-active --quiet blocks-fn-firewall.service \
  || die "blocks-fn-firewall.service is not active — the runner must not execute without it"
nft list table inet blocks_fn >/dev/null 2>&1 || die "nftables table inet blocks_fn is not loaded"
ok "egress firewall active, table inet blocks_fn loaded"

curl -fsS --max-time 5 "http://${REGISTRY_ADDR:-127.0.0.1:5000}/v2/" -o /dev/null \
  || die "the image registry is not answering"
ok "registry answering on ${REGISTRY_ADDR:-127.0.0.1:5000}"

# The runner writes a heartbeat hash into Redis every 5s and logs "Heartbeat failed" when
# it cannot. That is the only proof this host reached the Blocks Redis that does not need
# fnctl to hold a credential of its own, so it — not doctor's Redis check — is the gate.
# Scope the journal to the current invocation: a failure from the previous runner must not
# fail this deploy, and one from this runner must not be missed.
step "Runner heartbeat"
INVOCATION="$(systemctl show -p InvocationID --value "$UNIT")"
sleep 3   # the pid-stability check above already waited 5s; heartbeat interval is 5s
# Captured once, then matched: piping into `grep -q` would let grep exit on the first
# hit and SIGPIPE journalctl, and pipefail would report that as "no failures found" —
# the failure mode that silently passes a broken deploy.
JOURNAL="$(journalctl _SYSTEMD_INVOCATION_ID="$INVOCATION" --no-pager 2>/dev/null || true)"
if grep -q "Heartbeat failed" <<<"$JOURNAL"; then
  grep "Heartbeat failed" <<<"$JOURNAL" | tail -3
  die "the runner cannot reach Redis — heartbeats are failing (see above)"
fi
ok "heartbeat published — the runner reached Redis"

step "fnctl doctor"
fnctl doctor || die "fnctl doctor reported problems"

printf '\n\033[1mdeployed\033[0m in %ds — runner %s on %s\n' \
  "$(( $(date +%s) - START ))" "$(env_val RUNNER__RunnerId)" "$(hostname -s)"
[ "$WAS_ACTIVE" = yes ] && info "this replaced a running runner" || true
info "logs: make logs    state: make status    health: fnctl doctor"

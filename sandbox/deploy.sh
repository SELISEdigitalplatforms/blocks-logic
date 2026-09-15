#!/usr/bin/env bash
# deploy.sh — bare VM to a running Blocks Functions runner, in one command.
#
#   sudo ./deploy.sh                 provision, build, test, install, verify
#   sudo ./deploy.sh --skip-tests    same, without the test suites (faster redeploys)
#   sudo ./deploy.sh --env-file F    merge F into runner.env first, for an unattended deploy
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
SEED_ENV_FILE=
while [ $# -gt 0 ]; do
  case "$1" in
    --skip-tests) SKIP_TESTS=yes ;;
    --env-file)
      shift
      [ $# -gt 0 ] || die "--env-file needs a path"
      SEED_ENV_FILE="$1"
      ;;
    --env-file=*) SEED_ENV_FILE="${1#--env-file=}" ;;
    -h|--help) sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) die "unknown option '$1' (try --help)" ;;
  esac
  shift
done

need_root

# Checked here, before anything is installed. A bad path or a world-readable credentials file
# should cost nothing; discovering it after a full provisioning run is the opposite of that.
if [ -n "$SEED_ENV_FILE" ]; then
  [ -f "$SEED_ENV_FILE" ] || die "--env-file '$SEED_ENV_FILE' is not a file"
  [ -r "$SEED_ENV_FILE" ] || die "--env-file '$SEED_ENV_FILE' is not readable"
  # It holds the same credentials runner.env holds, so it gets the same standard. Refusing is
  # not pedantry: this file is usually left behind on the box afterwards. stat's %a drops
  # leading zeros, so pad before reading digits off the end — otherwise mode 0600 arrives as
  # "600" and mode 0060 as "60", and the same character position means two different things.
  SEED_PERMS="$(printf '%04d' "$(stat -c '%a' "$SEED_ENV_FILE")")"
  SEED_OTHER="${SEED_PERMS: -1}"
  SEED_GROUP="${SEED_PERMS: -2:1}"
  [ "$SEED_OTHER" = 0 ] \
    || die "--env-file '$SEED_ENV_FILE' is mode $SEED_PERMS — it holds credentials and must not be readable by other (chmod 600)"
  case "$SEED_GROUP" in
    [2367]) die "--env-file '$SEED_ENV_FILE' is mode $SEED_PERMS — it holds credentials and must not be group-writable (chmod 600)" ;;
  esac
  # Every line must be a comment, blank, or KEY=VALUE. Anything else is a file in the wrong
  # format, and merging it would write nonsense into runner.env one line at a time.
  BAD_LINE="$(grep -nvE '^[[:space:]]*(#.*)?$|^[[:space:]]*[A-Za-z_][A-Za-z0-9_]*=' "$SEED_ENV_FILE" | head -1 || true)"
  [ -z "$BAD_LINE" ] || die "--env-file '$SEED_ENV_FILE' line ${BAD_LINE%%:*} is not KEY=VALUE, a comment or blank"
fi

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

ENV_FILE=/etc/blocks-runner/runner.env
env_val() { sed -n "s/^[[:space:]]*$1=//p" "$ENV_FILE" | tail -1 | tr -d '"'"'"'' | tr -d '[:space:]'; }

# set_env_val <key> <value> [note] — set a key in runner.env, in place.
#
# Three cases, in order: the key is already live and its line is rewritten; the key is present
# but commented out — the template ships most keys that way — and that line becomes the live
# one, so the file never ends up with a commented placeholder above a real value contradicting
# it; or the key is absent and is appended under `note`.
#
# `cat >` rather than `mv`: the file is 0640 root:blocks-runner and replacing the inode would
# drop both. Returns 1 when the file already said exactly this, so the caller can stay quiet on
# a redeploy that changed nothing.
set_env_val() {
  local key="$1" val="$2" note="${3:-Written by deploy.sh.}" tmp
  tmp="$(mktemp)"
  if grep -qE "^[[:space:]]*${key}=" "$ENV_FILE"; then
    sed "s|^[[:space:]]*${key}=.*|${key}=${val}|" "$ENV_FILE" >"$tmp"
  elif grep -qE "^[[:space:]]*#[[:space:]]*${key}=" "$ENV_FILE"; then
    sed "0,\|^[[:space:]]*#[[:space:]]*${key}=|s||${key}=|; s|^${key}=.*|${key}=${val}|" "$ENV_FILE" >"$tmp"
  else
    cat "$ENV_FILE" >"$tmp"
    printf '\n# %s\n%s=%s\n' "$note" "$key" "$val" >>"$tmp"
  fi
  if cmp -s "$tmp" "$ENV_FILE"; then rm -f "$tmp"; return 1; fi
  cat "$tmp" >"$ENV_FILE"; rm -f "$tmp"; return 0
}

# The repository half of an image reference: no tag, no digest. A registry address carries a
# port, so the tag separator is only the last colon when it falls after the last slash.
image_repo() {
  local ref="${1%%@*}"
  case "${ref##*/}" in
    *:*) printf '%s' "${ref%:*}" ;;
    *)   printf '%s' "$ref" ;;
  esac
}

# The one thing this repo cannot supply is the credentials, and a fresh host therefore stops at
# phase 3 until a human fills them in. --env-file is how that human is a configuration system
# instead: the keys in it are merged into the runner.env phase 1 just created, so the same
# command provisions and configures in one pass. Values are never echoed.
if [ -n "$SEED_ENV_FILE" ]; then
  step "Seeding $ENV_FILE from $SEED_ENV_FILE"
  [ -f "$ENV_FILE" ] || die "$ENV_FILE does not exist after provisioning — cannot seed it"
  SEEDED=0
  # A pipe would run the loop in a subshell and lose the counter, and reading the file on fd 3
  # keeps stdin free for anything set_env_val does.
  while IFS= read -r seed_line <&3 || [ -n "$seed_line" ]; do
    # Comments and blanks fall out here: the pattern needs a bare KEY= at the start of the line.
    seed_key="$(printf '%s' "$seed_line" | sed -n 's/^[[:space:]]*\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p')"
    [ -n "$seed_key" ] || continue
    seed_val="${seed_line#*=}"
    if set_env_val "$seed_key" "$seed_val" "Seeded by deploy.sh --env-file."; then
      SEEDED=$((SEEDED + 1))
      info "set $seed_key"          # the key, never the value
    else
      info "unchanged: $seed_key"
    fi
  done 3<"$SEED_ENV_FILE"
  # Provisioning created runner.env 0640 root:blocks-runner and set_env_val writes in place, but
  # re-assert it: this is the one path that puts real credentials in the file.
  chown root:blocks-runner "$ENV_FILE"; chmod 0640 "$ENV_FILE"
  ok "seeded $SEEDED value(s) into $ENV_FILE"
fi

# --------------------------------------------------------------- 2. image ----
phase 2 "Runtime image"
"$HERE/runtime-image/build.sh"

# The image was just built and pushed, and build.sh recorded the digest it landed under. Pin
# the runner to that digest rather than to the tag it also carries: a tag is mutable, so a
# later `build.sh` silently changes what every subsequent tenant build is FROM — which is the
# one thing a base image must not do. Nothing else writes this line, so without it the runner
# falls back to the tag in RunnerOptions and the "pinned by digest in production" the docs
# promise was never true of any host.
step "Base image pin"
load_facts   # re-read: build.sh appended RUNTIME_IMAGE_DIGEST to .facts just now
if [ ! -f "$ENV_FILE" ]; then
  warn "$ENV_FILE does not exist yet — phase 3 will say so; not pinning the base image"
elif [ -z "${RUNTIME_IMAGE_DIGEST:-}" ]; then
  # Only --no-push skips the digest, and deploy.sh never passes it. Refuse rather than leave
  # the runner on a mutable tag without saying so.
  die "runtime-image/build.sh recorded no RUNTIME_IMAGE_DIGEST — cannot pin the base image"
else
  CURRENT_BASE="$(env_val RUNNER__BaseImage)"
  # Only ever re-pin a reference that already names this runtime's repository. An operator who
  # has pointed the host at a shared registry owns that line, and silently rewriting it to a
  # loopback address would break every build on the host.
  if [ -z "$CURRENT_BASE" ] || [ "$(image_repo "$CURRENT_BASE")" = "$(image_repo "$RUNTIME_IMAGE_DIGEST")" ]; then
    if set_env_val RUNNER__BaseImage "$RUNTIME_IMAGE_DIGEST" \
         "Written by deploy.sh from the runtime image it published."; then
      ok "base image pinned to $RUNTIME_IMAGE_DIGEST"
    else
      ok "base image already pinned to $RUNTIME_IMAGE_DIGEST"
    fi
  else
    warn "RUNNER__BaseImage is '$CURRENT_BASE', which is not this host's runtime repository — leaving it alone"
    warn "this host just published $RUNTIME_IMAGE_DIGEST; pin it by hand if that is what builds should use"
  fi
fi

# -------------------------------------------------------------- 3. config ----
# The one thing this repo cannot supply. A runner that starts without real credentials
# does not fail loudly — it sits in a retry loop looking healthy — so the gate is here,
# before anything is installed, rather than in the logs afterwards.
phase 3 "Configuration"
[ -f "$ENV_FILE" ] || die "$ENV_FILE is missing — run provision/40-runner-user.sh to seed it, then fill it in"

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

# Genesis validates delegated access at startup even for a worker that never calls IAM. A host
# missing this does not fail in a way that names it, so warn rather than let the operator read
# the symptom out of the journal. Not fatal: an environment may resolve it from the vault.
[ -n "$(env_val BLOCKS_IAM_BASE_URL)" ] \
  || warn "BLOCKS_IAM_BASE_URL is unset — Genesis validates delegated access at startup, and a host without it fails in a way that reads like something else"


# A base image that is not digest-pinned is not an error — a shared-registry host may legitimately
# track a tag — but it is worth saying out loud, because it is the difference between a tenant
# image built today and one built last month being FROM the same thing.
CONFIGURED_BASE="$(env_val RUNNER__BaseImage)"
case "$CONFIGURED_BASE" in
  "")         warn "RUNNER__BaseImage is unset — the runner will use its built-in default tag" ;;
  *@sha256:*) ok "base image: $CONFIGURED_BASE" ;;
  *)          warn "RUNNER__BaseImage '$CONFIGURED_BASE' is a tag, not a digest — what tenant builds are FROM can change under them" ;;
esac

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

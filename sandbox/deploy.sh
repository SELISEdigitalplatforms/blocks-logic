#!/usr/bin/env bash
# deploy.sh — bare VM to a running Blocks Functions runner, in one command.
#
#   sudo ./deploy.sh                 provision, build, test, install, verify
#   sudo ./deploy.sh --skip-tests    same, without the test suites (faster redeploys)
#   sudo ./deploy.sh --env-file F    merge F into runner.env first, for an unattended deploy
#   sudo ./deploy.sh --help
#
# Key Vault hosts (BLOCKS_VAULT_TYPE=2) use the VM's Azure Arc managed identity when it has one:
# deploy.sh installs the Azure CLI, signs it in as that identity for the service user, and the
# runner's credential chain picks the CLI sign-in up — runner.env then needs only
# KeyVault__KeyVaultUrl. Without a working Arc identity it needs KeyVault__ClientId,
# KeyVault__ClientSecret and KeyVault__TenantId. (Genesis 4.x, checked on 4.1.5 and 4.2.4, excludes managed identity from its
# credential chain, which is why the CLI route exists.)
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
    -h|--help) sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
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

# Set whenever something the runner reads only at startup changes — runner.env is an
# EnvironmentFile, so a change to it is invisible until the unit restarts. Phase 5 turns this
# into a restart even when the published binaries are byte-identical.
ENV_CHANGED=no

PHASES=6
phase() { printf '\n\033[1m[%d/%d] %s\033[0m\n' "$1" "$PHASES" "$2"; _logfile "PHASE $1/$PHASES $2"; }

START="$(date +%s)"

# ---------------------------------------------------------------- 1. host ----
phase 1 "Host provisioning"
# Before make, because make is what runs the phases and a minimal cloud image has none:
# without this the first command on a fresh host dies with "make: command not found"
# before preflight can explain anything. nftables is here for the mirror-image reason —
# preflight requires nft and nothing used to install it.
step "Bootstrap tools"
ensure_base_tools

# preflight, docker, gVisor, egress network + firewall, service user, registry, dotnet,
# log retention. Each script re-checks its own work and refuses to continue on failure.
make -s -C "$HERE/provision" all
# Facts are written by the scripts above, so read them only now.
load_facts

ENV_FILE=/etc/blocks-runner/runner.env
env_val() { sed -n "s/^[[:space:]]*$1=//p" "$ENV_FILE" | tail -1 | tr -d '"'"'"'' | tr -d '[:space:]'; }

# arc_identity_ok — can this host mint a Key Vault token from its Azure Arc managed identity?
#
# The Arc agent (himds) answers the first request with 401 and the path of a key file in the
# WWW-Authenticate header; proving you can read that file (root, or group himds) is the second
# half of the handshake. Any failure — no agent, agent down, wrong group, identity not granted
# — is "no", never an error: the caller falls back to client credentials. The token is matched,
# not printed. Responses are captured before matching: piping into `grep -q` would SIGPIPE curl
# and pipefail would misreport it.
arc_identity_ok() {
  local url hdr keyfile resp
  url="http://localhost:40342/metadata/identity/oauth2/token?api-version=2020-06-01&resource=https://vault.azure.net"
  hdr="$(curl -s -m 5 -D - -o /dev/null -H Metadata:true "$url" 2>/dev/null || true)"
  keyfile="$(printf '%s' "$hdr" | sed -n 's/^www-authenticate:.*realm=//Ip' | tr -d '\r' || true)"
  case "$keyfile" in /var/opt/azcmagent/tokens/*.key) ;; *) return 1 ;; esac
  [ -r "$keyfile" ] || return 1
  resp="$(curl -s -m 10 -H Metadata:true -H "Authorization: Basic $(cat "$keyfile")" "$url" 2>/dev/null || true)"
  grep -q '"access_token"' <<<"$resp"
}

# Why the Azure CLI: Genesis builds its Key Vault credential as a DefaultAzureCredential with the
# managed-identity step excluded, so the Arc identity cannot be used directly. The chain still
# contains the Azure CLI credential (what a developer's `az login` feeds), and the CLI can sign in
# as the Arc identity (`az login --identity`) and mint a fresh token on every call. Net effect: the
# service authenticates as the VM's identity with no client secret on disk. The sign-in lives in
# the service user's own config directory (0700), which no sandbox can reach.
azure_service_user() { printf '%s' "${RUNNER_USER:-blocks-runner}"; }
azure_config_dir() {
  local home; home="$(getent passwd "$(azure_service_user)" | cut -d: -f6)"
  printf '%s/.azure' "${home:-/var/lib/blocks-runner}"
}

# ensure_azure_cli — install the Azure CLI from Microsoft's apt repository when it is missing.
# Returns 1 (never dies) when it cannot, so the caller falls back to client credentials.
ensure_azure_cli() {
  command -v az >/dev/null 2>&1 && return 0
  [ "${OS_FAMILY:-}" = debian ] || { warn "Azure CLI install is only automated for apt hosts"; return 1; }
  local codename arch
  codename="$(. /etc/os-release && printf '%s' "${VERSION_CODENAME:-}")"
  arch="$(dpkg --print-architecture)"
  [ -n "$codename" ] || { warn "cannot tell this release's apt codename — not installing the Azure CLI"; return 1; }
  curl -fsS -m 20 -o /dev/null "https://packages.microsoft.com/repos/azure-cli/dists/${codename}/Release" \
    || { warn "Microsoft's Azure CLI repository has no packages for '${codename}'"; return 1; }
  step "Azure CLI"
  install -d -m 0755 /etc/apt/keyrings
  curl -fsSL -m 60 https://packages.microsoft.com/keys/microsoft.asc \
    | gpg --dearmor --yes -o /etc/apt/keyrings/microsoft.gpg || { warn "could not fetch Microsoft's signing key"; return 1; }
  chmod 0644 /etc/apt/keyrings/microsoft.gpg
  printf 'deb [arch=%s signed-by=/etc/apt/keyrings/microsoft.gpg] https://packages.microsoft.com/repos/azure-cli/ %s main\n' \
    "$arch" "$codename" >/etc/apt/sources.list.d/azure-cli.list
  DEBIAN_FRONTEND=noninteractive apt-get update -qq >/dev/null 2>&1 || true
  DEBIAN_FRONTEND=noninteractive apt-get install -y -qq azure-cli >/dev/null 2>&1 \
    || { warn "apt could not install azure-cli"; return 1; }
  command -v az >/dev/null 2>&1
}

# azure_cli_identity_ok — can the service user get a Key Vault token from the CLI, signed in as the
# Arc identity? Signs in first when it has to. Nothing is printed: -o none, and the token is not read.
azure_cli_identity_ok() {
  local svc cfg home grp
  svc="$(azure_service_user)"; cfg="$(azure_config_dir)"; home="$(dirname "$cfg")"; grp="$(id -gn "$svc")"
  install -d -m 0700 -o "$svc" -g "$grp" "$cfg"
  _az() {
    sudo -u "$svc" env HOME="$home" AZURE_CONFIG_DIR="$cfg" \
      IDENTITY_ENDPOINT="http://localhost:40342/metadata/identity/oauth2/token" IMDS_ENDPOINT="http://localhost:40342" \
      az "$@" --only-show-errors
  }
  _az account get-access-token --resource https://vault.azure.net -o none >/dev/null 2>&1 && return 0
  _az login --identity --allow-no-subscriptions -o none >/dev/null 2>&1 || return 1
  _az account get-access-token --resource https://vault.azure.net -o none >/dev/null 2>&1
}

# use_managed_identity — make the runner service able to use that identity itself.
#
# Azure.Identity recognises an Arc host by IDENTITY_ENDPOINT and IMDS_ENDPOINT. The agent exports
# them only to its own extensions, so a plain systemd service never sees them; they go in
# runner.env, together with AZURE_CONFIG_DIR for the CLI sign-in above. The service user also needs
# group himds to read the agent's key file. Each change is invisible to a running unit, so all of
# them mark ENV_CHANGED and phase 5 restarts it.
use_managed_identity() {
  if set_env_val AZURE_CONFIG_DIR "$(azure_config_dir)" \
       "Azure CLI sign-in (az login --identity) used by the Key Vault credential chain. Written by deploy.sh."; then
    ENV_CHANGED=yes; info "set AZURE_CONFIG_DIR"
  fi
  if set_env_val IDENTITY_ENDPOINT "http://localhost:40342/metadata/identity/oauth2/token" \
       "Azure Arc managed identity. Written by deploy.sh."; then
    ENV_CHANGED=yes; info "set IDENTITY_ENDPOINT"
  fi
  if set_env_val IMDS_ENDPOINT "http://localhost:40342" "Azure Arc managed identity. Written by deploy.sh."; then
    ENV_CHANGED=yes; info "set IMDS_ENDPOINT"
  fi
  local svc="${RUNNER_USER:-blocks-runner}"
  if getent group himds >/dev/null && ! id -nG "$svc" | tr ' ' '\n' | grep -qx himds; then
    usermod -aG himds "$svc"
    ENV_CHANGED=yes; info "added $svc to group himds"
  fi
}

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
      SEEDED=$((SEEDED + 1)); ENV_CHANGED=yes
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
      ENV_CHANGED=yes
      ok "base image pinned to $RUNTIME_IMAGE_DIGEST"
    else
      ok "base image already pinned to $RUNTIME_IMAGE_DIGEST"
    fi
  else
    warn "RUNNER__BaseImage is '$CURRENT_BASE', which is not this host's runtime repository — leaving it alone"
    warn "this host just published $RUNTIME_IMAGE_DIGEST; pin it by hand if that is what builds should use"
  fi
fi

# Same rule for the dependency-install image (sandbox/REUSE.md, "Any JS"). Without it the runner
# installs dependencies FROM the base image, which has no toolchain, so add-ons that compile fail.
if [ -f "$ENV_FILE" ] && [ -n "${BUILD_IMAGE_DIGEST:-}" ]; then
  CURRENT_BUILD="$(env_val RUNNER__BuildImage)"
  if [ -z "$CURRENT_BUILD" ] || [ "$(image_repo "$CURRENT_BUILD")" = "$(image_repo "$BUILD_IMAGE_DIGEST")" ]; then
    if set_env_val RUNNER__BuildImage "$BUILD_IMAGE_DIGEST" \
         "Written by deploy.sh from the build image it published."; then
      ENV_CHANGED=yes
      ok "build image pinned to $BUILD_IMAGE_DIGEST"
    else
      ok "build image already pinned to $BUILD_IMAGE_DIGEST"
    fi
  else
    warn "RUNNER__BuildImage is '$CURRENT_BUILD', which is not this host's runtime repository — leaving it alone"
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
    # The vault URL is needed either way. Preferred credential: this VM's Azure Arc managed identity,
    # reached through the Azure CLI (see azure_cli_identity_ok) because Genesis 4.x (checked 4.1.5, 4.2.4) excludes the
    # managed-identity step from its credential chain. Only when that does not work are client
    # credentials required. (If both exist, Genesis tries the client secret first.)
    require_val KeyVault__KeyVaultUrl
    MI_READY=no
    if arc_identity_ok; then
      use_managed_identity     # env + group himds first: the CLI sign-in below runs as the service user
      if ensure_azure_cli && azure_cli_identity_ok; then
        MI_READY=yes
        ok "vault: Azure Key Vault via the Arc managed identity (Azure CLI sign-in) — no client secret needed"
      else
        warn "the Arc identity answers, but the Azure CLI could not sign in with it; falling back to client credentials"
      fi
    else
      info "vault: Azure Key Vault — no Azure Arc managed identity on this host"
    fi
    if [ "$MI_READY" != yes ]; then
      for k in KeyVault__ClientId KeyVault__ClientSecret KeyVault__TenantId; do
        require_val "$k"
      done
      ok "vault: Azure Key Vault client credentials are configured"
    fi
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


# Secret-bound variables are resolved on this host at run start, through the same value store the
# control plane writes to. Without a vault URL the secrets SDK falls back to its Database store,
# which holds none of those values — the runner starts fine and every secret-bound run fails.
[ -n "$(env_val KeyVault__KeyVaultUrl)" ] || [ "$(env_val Secrets__ValueStore)" = "Database" ] \
  || warn "KeyVault__KeyVaultUrl is unset — runs whose variables are bound to secrets will fail as SECRET_UNRESOLVED"

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
PID_BEFORE=
if systemctl is-active --quiet blocks-function-runner.service; then
  WAS_ACTIVE=yes
  PID_BEFORE="$(systemctl show -p MainPID --value blocks-function-runner.service)"
fi
[ "$ENV_CHANGED" = no ] || info "runner.env changed — the unit will be restarted even if the binaries did not"
FORCE_RESTART="$ENV_CHANGED" make -s -C "$HERE" install

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
# Scope to this deploy, not just this invocation: install only restarts the unit when the
# binaries actually changed, so on a no-op redeploy the invocation is the one that has been
# running for days — and a heartbeat failure from a VPN blip last Tuesday must not fail a
# deploy that changed nothing.
HB_SINCE="$(date '+%Y-%m-%d %H:%M:%S')"
sleep 8   # two heartbeat intervals inside this window, whether or not the unit restarted
# Captured once, then matched: piping into `grep -q` would let grep exit on the first
# hit and SIGPIPE journalctl, and pipefail would report that as "no failures found" —
# the failure mode that silently passes a broken deploy.
JOURNAL="$(journalctl _SYSTEMD_INVOCATION_ID="$INVOCATION" --since "$HB_SINCE" --no-pager 2>/dev/null || true)"
if grep -q "Heartbeat failed" <<<"$JOURNAL"; then
  grep "Heartbeat failed" <<<"$JOURNAL" | tail -3
  die "the runner cannot reach Redis — heartbeats are failing (see above)"
fi
ok "heartbeat published — the runner reached Redis"

step "fnctl doctor"
fnctl doctor || die "fnctl doctor reported problems"

printf '\n\033[1mdeployed\033[0m in %ds — runner %s on %s\n' \
  "$(( $(date +%s) - START ))" "$(env_val RUNNER__RunnerId)" "$(hostname -s)"
if [ "$WAS_ACTIVE" = yes ]; then
  [ "$PID1" = "$PID_BEFORE" ] \
    && info "the runner was already current — it was not restarted" \
    || info "this replaced a running runner"
fi
info "logs: make logs    state: make status    health: fnctl doctor"

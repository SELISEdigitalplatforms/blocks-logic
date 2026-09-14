#!/usr/bin/env bash
# 40-runner-user.sh — the unprivileged service identity the runner runs as, its directories
# and the environment file skeleton. No credentials are written here; the file is a
# template the operator fills in (and it is the only place on the VM that holds any).
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_facts; load_versions

need_root
[ "${PREFLIGHT_OK:-no}" = yes ] || die "run ./00-preflight.sh first"
[ "${DOCKER_READY:-no}" = yes ] || die "run ./10-docker.sh first"

RUNNER_USER=blocks-runner
RUNNER_GROUP=blocks-runner
APP_DIR=/opt/blocks-function-runner
STATE_DIR=/var/lib/blocks-runner
CONF_DIR=/etc/blocks-runner
ENV_FILE="$CONF_DIR/runner.env"

step "Service identity"
if ! getent group "$RUNNER_GROUP" >/dev/null; then
  groupadd --system "$RUNNER_GROUP"; ok "created group $RUNNER_GROUP"
else info "group exists: $RUNNER_GROUP"; fi

if ! getent passwd "$RUNNER_USER" >/dev/null; then
  useradd --system --gid "$RUNNER_GROUP" --home-dir "$STATE_DIR" --no-create-home \
          --shell /usr/sbin/nologin --comment "Blocks Functions runner" "$RUNNER_USER"
  ok "created system user $RUNNER_USER (no shell, no home)"
else info "user exists: $RUNNER_USER"; fi

# Membership of `docker` is equivalent to root on this host. It is accepted deliberately:
# the runner must drive the Engine API, and the VM runs nothing else.
if [[ " $(id -nG "$RUNNER_USER") " == *" docker "* ]]; then
  info "$RUNNER_USER already in group docker"
else
  usermod -aG docker "$RUNNER_USER"; ok "$RUNNER_USER added to group docker"
fi
UID_N="$(id -u "$RUNNER_USER")"; GID_N="$(id -g "$RUNNER_USER")"
fact RUNNER_USER "$RUNNER_USER"; fact RUNNER_UID "$UID_N"; fact RUNNER_GID "$GID_N"
ok "$RUNNER_USER uid=$UID_N gid=$GID_N groups=$(id -nG "$RUNNER_USER" | tr ' ' ',')"

step "Directories"
# Binaries are root-owned and not writable by the service: a compromised runner cannot
# rewrite its own code.
install -d -m 0755 -o root -g root "$APP_DIR"
ok "$APP_DIR (root:root 0755)"

install -d -m 0750 -o "$RUNNER_USER" -g "$RUNNER_GROUP" "$STATE_DIR"
# runs/ and builds/ are 0751 so uid 10001 inside a sandbox can traverse into its own run
# directory to read the execution envelope, without being able to list the others.
install -d -m 0751 -o "$RUNNER_USER" -g "$RUNNER_GROUP" "$STATE_DIR/runs"
install -d -m 0750 -o "$RUNNER_USER" -g "$RUNNER_GROUP" "$STATE_DIR/builds"
ok "$STATE_DIR/{runs,builds}"

install -d -m 0750 -o root -g "$RUNNER_GROUP" "$CONF_DIR"
ok "$CONF_DIR (root:$RUNNER_GROUP 0750)"

step "Environment file"
if [ -f "$ENV_FILE" ]; then
  info "unchanged: $ENV_FILE (already present — not overwriting operator values)"
else
  write_file "$ENV_FILE" 0640 "root:$RUNNER_GROUP" <<ENV || true
# /etc/blocks-runner/runner.env — Blocks.FunctionRunner environment.
# Read by systemd (EnvironmentFile=). Contains the only credentials on this VM.
# Owned root:$RUNNER_GROUP, mode 0640. Never bind-mounted into a sandbox.

# --- Genesis bootstrap -------------------------------------------------------
# 1 = read secrets from the environment, 2 = Azure Key Vault.
BLOCKS_VAULT_TYPE=1
DOTNET_ENVIRONMENT=Production
# BlocksSecret__CacheConnectionString=
# BlocksSecret__DatabaseConnectionString=
# BlocksSecret__RootDatabaseName=

# --- Runner identity and paths ----------------------------------------------
RUNNER__RunnerId=$(hostname -s)
RUNNER__RunsDir=$STATE_DIR/runs
RUNNER__BuildsDir=$STATE_DIR/builds
RUNNER__Registry=127.0.0.1:5000
RUNNER__Network=${FN_NETWORK:-blocks-fn-egress}
RUNNER__ResolvConf=${FN_RESOLV_CONF:-$CONF_DIR/resolv.conf}
RUNNER__Runtime=runsc

# --- Admission ---------------------------------------------------------------
RUNNER__MaxActiveSandboxes=10
RUNNER__ReservedHostMemoryMb=2048

# --- Egress ------------------------------------------------------------------
# Informational: the enforced list lives in $CONF_DIR/deny-cidrs and is compiled
# into the nftables table by provision/30-network.sh.
RUNNER_DENY_CIDRS=
ENV
fi
chown "root:$RUNNER_GROUP" "$ENV_FILE"; chmod 0640 "$ENV_FILE"
ok "$ENV_FILE (root:$RUNNER_GROUP 0640)"

step "Verification"
[ "$(stat -c '%U:%G %a' "$ENV_FILE")" = "root:$RUNNER_GROUP 640" ] || die "$ENV_FILE permissions are wrong"
sudo -u "$RUNNER_USER" -g "$RUNNER_GROUP" test -r "$ENV_FILE" || die "$RUNNER_USER cannot read $ENV_FILE"
if sudo -u "$RUNNER_USER" test -w "$APP_DIR"; then die "$RUNNER_USER can write $APP_DIR — it must not"; fi
ok "$RUNNER_USER reads its env file and cannot write $APP_DIR"
sudo -u "$RUNNER_USER" docker version --format '{{.Server.Version}}' >/dev/null 2>&1 \
  || die "$RUNNER_USER cannot reach the Docker Engine API"
ok "$RUNNER_USER can drive the Docker Engine API"

fact RUNNER_USER_READY yes

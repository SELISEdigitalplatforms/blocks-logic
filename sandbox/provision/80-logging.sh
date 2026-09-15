#!/usr/bin/env bash
# 80-logging.sh — how long this host keeps logs, and the one place to change it.
#
# The runner has no metrics port and no log shipper: everything it says goes to the journal
# (StandardOutput=journal in the unit), and everything a container says goes to Docker's
# json-file driver. Both grow without bound by default, on a host that also stores function
# images, so both are capped here.
#
# The knobs live in /etc/blocks-runner/logging.conf, alongside deny-cidrs and dns-servers,
# and are read by two scripts:
#
#   JOURNAL_*  this script, which owns /etc/systemd/journald.conf.d/blocks-fn.conf
#   DOCKER_*   10-docker.sh, which owns /etc/docker/daemon.json
#
# One writer per file. This script only *checks* the Docker side and tells you to re-run
# `make -C provision docker` if daemon.json has drifted from logging.conf, because
# rewriting daemon.json means restarting the Engine and that is 10-docker.sh's call.
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_facts; load_versions

need_root
[ "${PREFLIGHT_OK:-no}" = yes ] || die "run ./00-preflight.sh first"
need_cmd jq

CONF=/etc/blocks-runner/logging.conf
# 99- so this sorts after every numbered drop-in and is the last word on these keys.
# Ubuntu images often ship one already: this host had 10-hostup-limits.conf pinning
# 14day/128M, which is exactly the kind of thing that must lose to logging.conf.
DROPIN=/etc/systemd/journald.conf.d/99-blocks-fn.conf

# ------------------------------------------------------------------ knobs ----
step "Configuration"
if [ ! -f "$CONF" ]; then
  write_file "$CONF" 0644 <<'CFG' || true
# logging.conf — log retention for the Runner VM.
# Re-run `make -C provision logging` after editing. If you change a DOCKER_ value,
# also run `make -C provision docker` (it owns daemon.json and restarts the Engine).

# --- systemd journal -----------------------------------------------------------
# Everything the runner logs lands here, so this is the number that matters.
# Any systemd time span: 1d, 12h, 7d, 4w. Empty means "no time limit".
JOURNAL_MAX_RETENTION=1d

# Hard disk ceiling, enforced regardless of the retention above. Accepts K/M/G.
# Matches the 128M the base image already enforced: retention is the policy, this is
# only the safety net. At the ~8M/day this host writes it should never bind.
JOURNAL_MAX_USE=128M

# --- Docker container logs -----------------------------------------------------
# json-file rotates by size, not by age — there is no time-based option — so these
# two are a size budget per container: max-size × max-file.
# Sandbox containers are removed after every run, so their logs are already
# short-lived; these bounds are really for the long-running infra containers.
DOCKER_LOG_MAX_SIZE=10m
DOCKER_LOG_MAX_FILE=3

# --- Docker build cache --------------------------------------------------------
# BuildKit's cache is the only thing on this host with no owner: ImageGc prunes
# function images and the registry, nothing prunes build layers. The daemon enforces
# this ceiling continuously, so it can never become the reason /var fills up.
# A share of the disk, not a fixed size — right on a 20 GB VM and on a 500 GB one.
# Floored at 512 MB internally; a cache smaller than one build only slows builds down.
DOCKER_BUILD_CACHE_MAX_PERCENT=2
CFG
fi

# shellcheck disable=SC1090
set -a; . "$CONF"; set +a

JOURNAL_MAX_RETENTION="${JOURNAL_MAX_RETENTION:-}"
JOURNAL_MAX_USE="${JOURNAL_MAX_USE:-128M}"
DOCKER_LOG_MAX_SIZE="${DOCKER_LOG_MAX_SIZE:-10m}"
DOCKER_LOG_MAX_FILE="${DOCKER_LOG_MAX_FILE:-3}"
DOCKER_BUILD_CACHE_MAX_PERCENT="${DOCKER_BUILD_CACHE_MAX_PERCENT:-2}"

# A typo here silently disables the limit, so refuse rather than pretend.
[ -z "$JOURNAL_MAX_RETENTION" ] || [[ "$JOURNAL_MAX_RETENTION" =~ ^[0-9]+(s|m|h|d|w|month|y)$ ]] \
  || die "JOURNAL_MAX_RETENTION='$JOURNAL_MAX_RETENTION' is not a systemd time span (e.g. 1d, 12h, 4w)"
[[ "$JOURNAL_MAX_USE" =~ ^[0-9]+[KMG]$ ]] \
  || die "JOURNAL_MAX_USE='$JOURNAL_MAX_USE' is not a size (e.g. 512M, 2G)"
[[ "$DOCKER_LOG_MAX_SIZE" =~ ^[0-9]+[kmg]$ ]] \
  || die "DOCKER_LOG_MAX_SIZE='$DOCKER_LOG_MAX_SIZE' is not a Docker size (e.g. 10m, 1g)"
[[ "$DOCKER_LOG_MAX_FILE" =~ ^[0-9]+$ ]] \
  || die "DOCKER_LOG_MAX_FILE='$DOCKER_LOG_MAX_FILE' is not a number"
{ [[ "$DOCKER_BUILD_CACHE_MAX_PERCENT" =~ ^[0-9]+$ ]] \
  && [ "$DOCKER_BUILD_CACHE_MAX_PERCENT" -ge 1 ] && [ "$DOCKER_BUILD_CACHE_MAX_PERCENT" -le 50 ]; } \
  || die "DOCKER_BUILD_CACHE_MAX_PERCENT='$DOCKER_BUILD_CACHE_MAX_PERCENT' is not a whole percentage between 1 and 50"

info "journal: retention ${JOURNAL_MAX_RETENTION:-unlimited}, ceiling $JOURNAL_MAX_USE"
info "docker:  ${DOCKER_LOG_MAX_SIZE} × ${DOCKER_LOG_MAX_FILE} per container"
info "build cache: ${DOCKER_BUILD_CACHE_MAX_PERCENT}% of the disk = $(build_cache_max_mb "$DOCKER_BUILD_CACHE_MAX_PERCENT")MB"

# ---------------------------------------------------------------- journald ----
step "systemd-journald"
# Storage=persistent is deliberate: the alternative is a journal that lives in RAM and
# disappears on reboot, which is the opposite of what a retention policy is for.
changed=yes
write_file "$DROPIN" 0644 <<CONF_EOF || changed=no
# Generated by provision/80-logging.sh — do not edit by hand.
# Edit /etc/blocks-runner/logging.conf and re-run that script instead.
[Journal]
Storage=persistent
MaxRetentionSec=${JOURNAL_MAX_RETENTION}
SystemMaxUse=${JOURNAL_MAX_USE}
CONF_EOF

if [ "$changed" = yes ]; then
  systemctl restart systemd-journald || die "could not restart systemd-journald"
  ok "journald restarted with the new policy"
fi

# The policy applies to new writes; vacuum makes it true for what is already on disk.
if [ -n "$JOURNAL_MAX_RETENTION" ]; then
  BEFORE="$(journalctl --disk-usage 2>/dev/null | grep -oE '[0-9.]+[KMG]' | tail -1 || echo '?')"
  journalctl --vacuum-time="$JOURNAL_MAX_RETENTION" >/dev/null 2>&1 || warn "vacuum by time failed"
  journalctl --vacuum-size="$JOURNAL_MAX_USE"       >/dev/null 2>&1 || warn "vacuum by size failed"
  AFTER="$(journalctl --disk-usage 2>/dev/null | grep -oE '[0-9.]+[KMG]' | tail -1 || echo '?')"
  ok "journal vacuumed to $JOURNAL_MAX_RETENTION / $JOURNAL_MAX_USE (${BEFORE} -> ${AFTER})"
fi

# ------------------------------------------------------------------ docker ----
step "Docker log options"
CUR_SIZE="$(jq -r '."log-opts"."max-size" // empty' /etc/docker/daemon.json 2>/dev/null || true)"
CUR_FILE="$(jq -r '."log-opts"."max-file" // empty' /etc/docker/daemon.json 2>/dev/null || true)"
# Either spelling, because which one daemon.json carries depends on the Engine's version.
CUR_CACHE="$(jq -r '(.builder.gc.policy[0] | (.maxUsedSpace // .keepStorage)) // empty' /etc/docker/daemon.json 2>/dev/null || true)"
WANT_CACHE="$(build_cache_max_mb "$DOCKER_BUILD_CACHE_MAX_PERCENT")MB"
if [ "$CUR_SIZE" = "$DOCKER_LOG_MAX_SIZE" ] && [ "$CUR_FILE" = "$DOCKER_LOG_MAX_FILE" ] && [ "$CUR_CACHE" = "$WANT_CACHE" ]; then
  ok "daemon.json matches logging.conf ($CUR_SIZE × $CUR_FILE, build cache $CUR_CACHE)"
else
  warn "daemon.json has ${CUR_SIZE:-unset} × ${CUR_FILE:-unset} and build cache ${CUR_CACHE:-unset};"
  warn "logging.conf wants $DOCKER_LOG_MAX_SIZE × $DOCKER_LOG_MAX_FILE and build cache $WANT_CACHE"
  warn "run \`make -C provision docker\` to apply it (this restarts the Engine)"
fi
# Existing containers keep the options they were created with; only new ones pick these up.

# ------------------------------------------------------------ verification ----
step "Verification"
EFF_RET="$(journalctl --header 2>/dev/null >/dev/null; systemd-analyze cat-config systemd/journald.conf 2>/dev/null | grep -E '^\s*MaxRetentionSec=' | tail -1 | cut -d= -f2)"
[ "$EFF_RET" = "$JOURNAL_MAX_RETENTION" ] \
  || die "effective MaxRetentionSec is '${EFF_RET:-unset}', expected '$JOURNAL_MAX_RETENTION'"
ok "effective journald retention: $EFF_RET"

fact LOG_JOURNAL_RETENTION "$JOURNAL_MAX_RETENTION"
fact LOG_JOURNAL_MAX_USE "$JOURNAL_MAX_USE"
fact LOG_DOCKER_MAX_SIZE "$DOCKER_LOG_MAX_SIZE"
fact LOG_DOCKER_MAX_FILE "$DOCKER_LOG_MAX_FILE"
fact LOGGING_READY yes
ok "logging policy applied"

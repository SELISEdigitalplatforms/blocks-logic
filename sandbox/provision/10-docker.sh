#!/usr/bin/env bash
# 10-docker.sh — Docker Engine, containerd and buildx from the Ubuntu archive, plus the
# daemon configuration the sandbox depends on. Idempotent; safe to re-run.
#
# The runsc runtime is declared here but its binaries are installed by 20-gvisor.sh.
# dockerd resolves a runtime's path only when a container asks for it, so declaring it
# early is harmless and keeps one canonical daemon.json.
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_facts; load_versions

need_root
[ "${PREFLIGHT_OK:-no}" = yes ] || die "run ./00-preflight.sh first"

detect_os

# Docker's own repository, added only when the distro's Engine is too old to be used.
DOCKER_KEYRING=/etc/apt/keyrings/docker.asc
DOCKER_LIST=/etc/apt/sources.list.d/blocks-docker.list
ensure_docker_repo() {
  install -d -m 0755 /etc/apt/keyrings
  if [ -s "$DOCKER_KEYRING" ]; then
    info "unchanged: $DOCKER_KEYRING"
  else
    retry 3 5 -- curl -fsSL --connect-timeout 15 --max-time 120 \
      "https://download.docker.com/linux/${OS_ID}/gpg" -o "$DOCKER_KEYRING.part" \
      || die "could not fetch Docker's apt signing key for $OS_ID"
    mv "$DOCKER_KEYRING.part" "$DOCKER_KEYRING"
    chmod 0644 "$DOCKER_KEYRING"
    ok "installed Docker's apt signing key"
  fi
  write_file "$DOCKER_LIST" 0644 <<LIST || true
deb [arch=amd64 signed-by=$DOCKER_KEYRING] https://download.docker.com/linux/$OS_ID $OS_CODENAME stable
LIST
  # A source list that has just appeared makes every cached candidate stale.
  APT_UPDATED=0
}

step "Packages"
# Needed by the scripts after this one (20-gvisor unpacks a .tar.zstd, 80-logging reads
# daemon.json with jq), installed here because this is the first script that installs.
pkg_install zstd jq uidmap

# Which Engine: the distro's when it is 20.10 or newer — the first release that speaks
# cgroup v2 and honours the keys this daemon.json sets — otherwise Docker's own repository.
# Ubuntu 20.04 and Debian 10 are the releases that take the second path.
DOCKER_CANDIDATE="$(pkg_candidate docker.io || true)"
if [ -n "$DOCKER_CANDIDATE" ] && version_ge "${DOCKER_CANDIDATE%%[-+~]*}" 20.10; then
  DOCKER_SOURCE=distro
  pkg_install docker.io containerd
  # docker-buildx is packaged from Debian 12 / Ubuntu 23.04 onwards. Where it is missing,
  # the Engine's built-in BuildKit (20.10+) is what runtime-image/build.sh actually uses.
  if pkg_available docker-buildx; then
    pkg_install docker-buildx
  else
    info "docker-buildx is not packaged on $OS_ID $OS_VERSION_ID - using the Engine's built-in BuildKit"
  fi
else
  DOCKER_SOURCE=docker.com
  info "distro docker.io is ${DOCKER_CANDIDATE:-absent}, older than 20.10 - taking docker-ce from download.docker.com"
  ensure_docker_repo
  pkg_install docker-ce docker-ce-cli containerd.io docker-buildx-plugin
fi
fact DOCKER_SOURCE "$DOCKER_SOURCE"
ok "docker from: $DOCKER_SOURCE"

step "Daemon configuration"
PLATFORM="${GVISOR_PLATFORM:-systrap}"
# Container log bounds are shared with 80-logging.sh, which owns the journal side of the
# same policy. This script is the only writer of daemon.json; that one only checks it.
LOGGING_CONF=/etc/blocks-runner/logging.conf
# shellcheck disable=SC1090
[ -f "$LOGGING_CONF" ] && { set -a; . "$LOGGING_CONF"; set +a; } || true
DOCKER_LOG_MAX_SIZE="${DOCKER_LOG_MAX_SIZE:-10m}"
DOCKER_LOG_MAX_FILE="${DOCKER_LOG_MAX_FILE:-3}"

# The BuildKit cache ceiling, owned by logging.conf like the other size bounds. The daemon
# enforces it continuously; a timer would only ever run after the disk was already full.
BUILD_CACHE_PERCENT="${DOCKER_BUILD_CACHE_MAX_PERCENT:-2}"
{ [[ "$BUILD_CACHE_PERCENT" =~ ^[0-9]+$ ]] && [ "$BUILD_CACHE_PERCENT" -ge 1 ] && [ "$BUILD_CACHE_PERCENT" -le 50 ]; } \
  || die "DOCKER_BUILD_CACHE_MAX_PERCENT='$BUILD_CACHE_PERCENT' is not a whole percentage between 1 and 50"
BUILD_CACHE_MAX_MB="$(build_cache_max_mb "$BUILD_CACHE_PERCENT")"
# maxUsedSpace is the current spelling; keepStorage is what daemons before 28 understand, and
# is deprecated rather than removed after it. Pick by what is actually running on this host.
DOCKER_SERVER_VERSION="$(docker version --format '{{.Server.Version}}' 2>/dev/null \
  || docker --version 2>/dev/null | sed -n 's/.*version \([0-9.]*\).*/\1/p')"
if [ -n "$DOCKER_SERVER_VERSION" ] && version_ge "$DOCKER_SERVER_VERSION" 28; then
  BUILD_CACHE_FIELD=maxUsedSpace
else
  BUILD_CACHE_FIELD=keepStorage
fi
fact BUILD_CACHE_MAX_MB "$BUILD_CACHE_MAX_MB"
info "build cache ceiling: ${BUILD_CACHE_MAX_MB}MB (${BUILD_CACHE_PERCENT}% of the disk behind /var/lib/docker), via $BUILD_CACHE_FIELD"
# Address pool is fixed so generated bridges never collide with 172.29.0.0/24
# (blocks-fn-egress) or with the VPN ranges added later.
cfg_changed=yes
write_file /etc/docker/daemon.json 0644 <<JSON || cfg_changed=no
{
  "default-runtime": "runc",
  "runtimes": {
    "runsc": {
      "path": "/usr/local/bin/runsc",
      "runtimeArgs": [
        "--platform=${PLATFORM}",
        "--network=sandbox",
        "--overlay2=root:memory",
        "--sidecar-usage-policy=STRICT",
        "--sidecar-release-enforcement-policy=ALWAYS"
      ]
    }
  },
  "live-restore": true,
  "userland-proxy": false,
  "no-new-privileges": true,
  "icc": false,
  "log-driver": "json-file",
  "log-opts": { "max-size": "${DOCKER_LOG_MAX_SIZE}", "max-file": "${DOCKER_LOG_MAX_FILE}" },
  "default-address-pools": [
    { "base": "172.28.0.0/16", "size": 24 }
  ],
  "default-ulimits": {
    "nofile": { "Name": "nofile", "Hard": 8192, "Soft": 8192 }
  },
  "ipv6": false,
  "features": { "buildkit": true },
  "builder": {
    "gc": {
      "enabled": true,
      "policy": [
        { "all": true, "${BUILD_CACHE_FIELD}": "${BUILD_CACHE_MAX_MB}MB" }
      ]
    }
  }
}
JSON

# This file now carries a key chosen from the daemon's own version, so prove the daemon
# accepts it before a restart puts it into service. --validate exists from Docker 23.0.
if VALIDATE_OUT="$(dockerd --validate --config-file=/etc/docker/daemon.json 2>&1)"; then
  ok "daemon.json validated"
else
  case "$VALIDATE_OUT" in
    *"unknown flag"*|*"flag provided but not defined"*)
      info "this dockerd has no --validate - skipping the configuration check" ;;
    *) die "daemon.json is not valid: $VALIDATE_OUT" ;;
  esac
fi

step "Service"
systemctl daemon-reload
if [ "$cfg_changed" = yes ] && systemctl is-active --quiet docker; then
  info "daemon.json changed — restarting docker"
  systemctl restart docker
fi
systemd_enable containerd.service
systemd_enable docker.service

step "Verification"
retry 10 2 -- docker info >/dev/null 2>&1 || die "docker is not answering"
DV="$(docker version --format '{{.Server.Version}}')"
CV="$(containerd --version | awk '{print $3}')"
BV="$(docker buildx version 2>/dev/null | awk '{print $2}')"
fact DOCKER_VERSION "$DV"; fact CONTAINERD_VERSION "$CV"; fact BUILDX_VERSION "${BV:-none}"
ok "docker $DV · containerd $CV · buildx ${BV:-none}"

[ "$(docker info --format '{{.DefaultRuntime}}')" = runc ] || die "default runtime is not runc"
ok "default runtime is runc"
RUNTIMES_JSON="$(docker info --format '{{json .Runtimes}}')"
grep -q '"runsc"' <<<"$RUNTIMES_JSON" || die "runsc runtime not declared in daemon.json"
ok "runsc runtime declared (binaries installed by 20-gvisor.sh)"
[ "$(docker info --format '{{.LiveRestoreEnabled}}')" = true ] || die "live-restore not enabled"
[ "$(docker info --format '{{.CgroupVersion}}')" = 2 ] || die "docker is not on cgroup v2"
ok "live-restore on, cgroup v2 driver"

fact DOCKER_READY yes

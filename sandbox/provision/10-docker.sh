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

step "Packages"
apt_install docker.io docker-buildx containerd zstd jq uidmap

step "Daemon configuration"
PLATFORM="${GVISOR_PLATFORM:-systrap}"
# Container log bounds are shared with 80-logging.sh, which owns the journal side of the
# same policy. This script is the only writer of daemon.json; that one only checks it.
LOGGING_CONF=/etc/blocks-runner/logging.conf
# shellcheck disable=SC1090
[ -f "$LOGGING_CONF" ] && { set -a; . "$LOGGING_CONF"; set +a; } || true
DOCKER_LOG_MAX_SIZE="${DOCKER_LOG_MAX_SIZE:-10m}"
DOCKER_LOG_MAX_FILE="${DOCKER_LOG_MAX_FILE:-3}"
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
  "features": { "buildkit": true }
}
JSON

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

#!/usr/bin/env bash
# 20-gvisor.sh — install the pinned gVisor release and prove a container runs under it.
#
# Release 20260831 no longer publishes standalone `runsc` / `containerd-shim-runsc-v1`
# objects; it publishes gvisor.tar.zstd (+ .sha512) only, and this build delegates work to
# sidecar binaries it expects in `gvisor-bin/` next to runsc. It can fetch missing sidecars
# over the network at container-create time, which we do not want on a sandbox host, so the
# whole tree is installed from the verified bundle and runsc runs with
# --sidecar-usage-policy=STRICT (see /etc/docker/daemon.json): missing sidecar => refuse.
#
# Fails closed: if runsc cannot be installed or cannot run a container, nothing is left
# half-installed that would let the runner think gVisor is available.
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_facts; load_versions

need_root
[ "${PREFLIGHT_OK:-no}" = yes ] || die "run ./00-preflight.sh first"
[ "${DOCKER_READY:-no}" = yes ] || die "run ./10-docker.sh first"
need_cmd zstd

CACHE="/var/cache/blocks-fn/gvisor/${GVISOR_RELEASE}"
URL="${GVISOR_BASE_URL}/${GVISOR_RELEASE}/${GVISOR_ARCH}/${GVISOR_BUNDLE}"
BIN=/usr/local/bin
SIDECAR_DIR="$BIN/gvisor-bin"

step "Download (pinned ${GVISOR_RELEASE}, sha512 verified)"
download_verified "$URL" "$CACHE/${GVISOR_BUNDLE}" "$GVISOR_BUNDLE_SHA512"

step "Install"
STAGE="$CACHE/stage"
rm -rf "$STAGE"; install -d -m 0755 "$STAGE"
tar --zstd -xf "$CACHE/${GVISOR_BUNDLE}" -C "$STAGE"
for f in runsc containerd-shim-runsc-v1; do
  [ -f "$STAGE/$f" ] || die "bundle does not contain $f"
done
[ -d "$STAGE/gvisor-bin" ] || die "bundle does not contain gvisor-bin/ sidecars"

install_if_changed() { # <src> <dest>
  if [ -f "$2" ] && cmp -s "$1" "$2"; then info "unchanged: $2"; return 1; fi
  install -m 0755 -o root -g root "$1" "$2"; ok "installed: $2"; return 0
}
# Whether anything actually landed decides whether the Engine is restarted below.
RUNSC_CHANGED=no
install_if_changed "$STAGE/runsc" "$BIN/runsc" && RUNSC_CHANGED=yes || true
install_if_changed "$STAGE/containerd-shim-runsc-v1" "$BIN/containerd-shim-runsc-v1" && RUNSC_CHANGED=yes || true
install -d -m 0755 -o root -g root "$SIDECAR_DIR"
for f in "$STAGE"/gvisor-bin/*; do
  install_if_changed "$f" "$SIDECAR_DIR/$(basename "$f")" && RUNSC_CHANGED=yes || true
done
# Nothing but root may replace the runtime or its sidecars.
chown -R root:root "$BIN/runsc" "$BIN/containerd-shim-runsc-v1" "$SIDECAR_DIR"
chmod 0755 "$BIN/runsc" "$BIN/containerd-shim-runsc-v1"
rm -rf "$STAGE"

RUNSC_VERSION="$("$BIN/runsc" --version | awk '/^runsc version/{print $3}')"
fact RUNSC_VERSION "$RUNSC_VERSION"
fact RUNSC_PATH "$BIN/runsc"
fact GVISOR_SIDECAR_DIR "$SIDECAR_DIR"
ok "runsc $RUNSC_VERSION on platform ${GVISOR_PLATFORM}"
[ "${RUNSC_VERSION#release-}" = "${GVISOR_RELEASE}.0" ] \
  || warn "runsc reports $RUNSC_VERSION for pinned release $GVISOR_RELEASE"

step "Daemon wiring"
grep -q '"runsc"' /etc/docker/daemon.json || die "runsc runtime missing from /etc/docker/daemon.json"

# Restarting the Engine is never free here. blocks-fn-firewall.service is PartOf=docker.service
# and the runner BindsTo the firewall, so a restart at this point stops the runner — which
# phase 5 only puts back minutes later, after the configuration gate and the tests. It also
# interrupts every sandbox on the host. So: only when runsc actually changed, or when the
# daemon does not know the runtime yet.
DOCKER_KNOWS_RUNSC=no
docker info --format '{{json .Runtimes}}' 2>/dev/null | grep -q '"runsc"' && DOCKER_KNOWS_RUNSC=yes || true
if [ "$RUNSC_CHANGED" = yes ] || [ "$DOCKER_KNOWS_RUNSC" = no ]; then
  info "restarting docker (runsc changed: $RUNSC_CHANGED, runtime already known: $DOCKER_KNOWS_RUNSC)"
  RUNNER_WAS_ACTIVE=no; runner_active && RUNNER_WAS_ACTIVE=yes || true
  systemctl restart docker
  retry 10 2 -- docker info >/dev/null 2>&1 || die "docker did not come back after restart"
  runner_resume "$RUNNER_WAS_ACTIVE"
else
  info "runsc unchanged and the Engine already exposes it - not restarting docker"
fi
RUNTIMES_JSON="$(docker info --format '{{json .Runtimes}}')"
grep -q '"runsc"' <<<"$RUNTIMES_JSON" || die "docker does not know the runsc runtime"
ok "docker exposes the runsc runtime"

step "Smoke test"
docker image inspect "$PROBE_IMAGE" >/dev/null 2>&1 || retry 3 5 -- docker pull -q "$PROBE_IMAGE" >/dev/null
OUT="$(docker run --rm --runtime=runsc "$PROBE_IMAGE" sh -c 'uname -r; id -u' 2>&1)" \
  || die "docker run --runtime=runsc failed — gVisor is not usable:
$OUT"
KERNEL_IN="${OUT%%$'\n'*}"
case "$(printf '%s' "$KERNEL_IN" | tr 'A-Z' 'a-z')" in
  *gvisor*) ok "container kernel reports gVisor: $KERNEL_IN" ;;
  *) die "container kernel is '$KERNEL_IN', not gVisor — refusing to continue" ;;
esac

# Prove the sidecar policy is strict: a sandbox must not be able to fetch anything at start.
docker run --rm --runtime=runsc "$PROBE_IMAGE" true >/dev/null 2>&1 \
  || die "strict sidecar policy rejected a plain container — check $SIDECAR_DIR"
ok "sidecars resolved locally under --sidecar-usage-policy=STRICT"

fact GVISOR_READY yes
ok "gVisor ready"

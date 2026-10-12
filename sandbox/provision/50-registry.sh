#!/usr/bin/env bash
# 50-registry.sh — the local, immutable image store for built function images.
# Bound to 127.0.0.1:5000 only: nothing off-host can read or push, and no tenant sandbox
# can reach it either (the firewall drops container-to-host traffic outright).
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_facts; load_versions

need_root
[ "${DOCKER_READY:-no}" = yes ] || die "run ./10-docker.sh first"

NAME=blocks-fn-registry
VOLUME=blocks-fn-registry-data
ADDR=127.0.0.1:5000

step "Image"
docker image inspect "$REGISTRY_IMAGE" >/dev/null 2>&1 || retry 3 5 -- docker pull -q "$REGISTRY_IMAGE" >/dev/null
REG_DIGEST="$(docker image inspect "$REGISTRY_IMAGE" --format '{{index .RepoDigests 0}}' 2>/dev/null || echo "$REGISTRY_IMAGE")"
fact REGISTRY_IMAGE_DIGEST "$REG_DIGEST"
ok "$REG_DIGEST"

step "Volume"
docker volume inspect "$VOLUME" >/dev/null 2>&1 || { docker volume create "$VOLUME" >/dev/null; ok "created volume $VOLUME"; }
info "volume: $VOLUME"

step "Container"
recreate=no
if docker container inspect "$NAME" >/dev/null 2>&1; then
  CUR_IMG="$(docker container inspect "$NAME" --format '{{.Config.Image}}')"
  CUR_BIND="$(docker container inspect "$NAME" --format '{{range $p, $c := .HostConfig.PortBindings}}{{range $c}}{{.HostIp}}:{{.HostPort}}{{end}}{{end}}')"
  if [ "$CUR_IMG" != "$REGISTRY_IMAGE" ] || [ "$CUR_BIND" != "$ADDR" ]; then
    warn "$NAME differs (image=$CUR_IMG bind=$CUR_BIND) — recreating"
    recreate=yes
  else
    info "unchanged: $NAME"
  fi
else recreate=yes; fi

if [ "$recreate" = yes ]; then
  docker rm -f "$NAME" >/dev/null 2>&1 || true
  # Runs on the default runtime (runc): this is infrastructure, not tenant code.
  docker run -d --name "$NAME" \
    --restart unless-stopped \
    -p "$ADDR:5000" \
    -v "$VOLUME:/var/lib/registry" \
    -e REGISTRY_STORAGE_DELETE_ENABLED=true \
    -e REGISTRY_HTTP_ADDR=0.0.0.0:5000 \
    -e REGISTRY_LOG_LEVEL=warn \
    --health-cmd 'wget -q -O /dev/null http://127.0.0.1:5000/v2/ || exit 1' \
    --health-interval 30s --health-timeout 5s --health-retries 3 \
    "$REGISTRY_IMAGE" >/dev/null
  ok "started $NAME on $ADDR (deletion enabled, volume $VOLUME)"
fi

step "Verification"
retry 15 2 -- curl -fsS --max-time 5 "http://$ADDR/v2/" -o /dev/null || die "registry is not answering on $ADDR/v2/"
ok "registry answers on http://$ADDR/v2/"
# Must not be listening on anything but loopback.
# The -n guard matters: a here-string of "" still feeds grep one empty line, which
# `grep -v` would match, turning "nothing is listening" into a false alarm.
LISTENERS="$(ss -ltnH "sport = :5000" | awk '{print $4}')"
if [ -n "$LISTENERS" ] && grep -qv '^127\.0\.0\.1:' <<<"$LISTENERS"; then
  die "something is listening on :5000 outside loopback"
fi
ok "bound to loopback only"

fact REGISTRY_ADDR "$ADDR"; fact REGISTRY_READY yes

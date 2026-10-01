#!/usr/bin/env bash
# build.sh — build, test and publish the Node 24 runtime image.
#
#   ./build.sh            build, run the suite under gVisor, push to the local registry
#   ./build.sh --no-push  build and test only
#
# The image is pushed to 127.0.0.1:5000 and its digest recorded in provision/.facts as
# RUNTIME_IMAGE_DIGEST, so tenant builds can pin `FROM …@sha256:…` rather than a tag.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$(cd "$HERE/../provision" && pwd)/lib.sh"
load_facts

need_root
[ "${GVISOR_READY:-no}" = yes ] || die "run provision/20-gvisor.sh first"

TAG="${TAG:-24-v1}"
LOCAL="blocks-functions-node:$TAG"
TEST_LOCAL="blocks-functions-node:$TAG-test"
REGISTRY="${REGISTRY_ADDR:-127.0.0.1:5000}"
REMOTE="$REGISTRY/blocks/functions-node:$TAG"
PUSH=yes
[ "${1:-}" = "--no-push" ] && PUSH=no

step "Build"
docker build --target runtime -t "$LOCAL" "$HERE" >/dev/null || die "runtime image build failed"
ok "$LOCAL"
docker build --target test -t "$TEST_LOCAL" "$HERE" >/dev/null || die "test image build failed"
ok "$TEST_LOCAL"

step "Runtime test suite (Node 24, under gVisor)"
# /tmp is exec here because node:test spawns the bootstrap from a temporary directory; a
# tenant container never gets that.
docker run --rm --runtime=runsc --network=none --read-only \
  --tmpfs /tmp:rw,exec,nosuid,nodev,size=64m \
  --memory=512m --pids-limit=128 --cap-drop=ALL \
  --security-opt no-new-privileges --user 10001:10001 \
  "$TEST_LOCAL" | tail -12
ok "suite passed"

step "Image contract"
insp() { docker image inspect "$LOCAL" --format "$1"; }
[ "$(insp '{{.Config.User}}')" = "10001:10001" ] || die "image user is not 10001:10001"
[ "$(insp '{{.Config.Entrypoint}}')" = "[node /runtime/bootstrap.mjs]" ] || die "unexpected entrypoint"
NODE_IN="$(docker run --rm --entrypoint node "$LOCAL" --version)"
case "$NODE_IN" in v24.*) ok "runtime is Node $NODE_IN (uid 10001, bootstrap entrypoint)" ;;
  *) die "expected Node 24, image has $NODE_IN" ;; esac
# Nothing in the trusted runtime may be writable by the function's uid.
docker run --rm --read-only --user 10001:10001 --entrypoint sh "$LOCAL" \
  -c 'test ! -w /runtime && test ! -w /runtime/bootstrap.mjs' \
  || die "/runtime is writable by uid 10001"
ok "/runtime is not writable by the function uid"

if [ "$PUSH" = yes ]; then
  step "Publish"
  retry 10 2 -- curl -fsS --max-time 5 "http://$REGISTRY/v2/" -o /dev/null \
    || die "local registry $REGISTRY is not answering — run provision/50-registry.sh"
  docker tag "$LOCAL" "$REMOTE"
  docker push -q "$REMOTE" >/dev/null || die "push to $REMOTE failed"
  DIGEST="$(docker image inspect "$REMOTE" --format '{{index .RepoDigests 0}}')"
  fact RUNTIME_IMAGE "$REMOTE"
  fact RUNTIME_IMAGE_DIGEST "$DIGEST"
  fact RUNTIME_NODE_VERSION "$NODE_IN"
  ok "$DIGEST"
fi

fact RUNTIME_IMAGE_READY yes
ok "runtime image ready"

#!/usr/bin/env bash
# any-js probe: runs index.js in the real runtime image under gVisor, at the production limits
# (0.1 CPU, 128 MB, read-only root, 64 MB noexec /tmp, uid 10001), once per case in single-run mode
# and then all cases in ONE reused sandbox. Needs: docker + runsc, `npm install` done here, and
# reachable MONGO_URL / REDIS_URL / PG_URL / HTTP_URL (defaults: the local test containers
# drv-mongo / drv-redis / drv-pg on docker network $NET — see the bottom of this file).
#
#   ./run.sh                        # local test containers
#   NET=bridge MONGO_URL=... ./run.sh
#
# This checks what the RUNTIME can do. It does not use the runner's egress network
# (blocks-fn-egress), which only lets public addresses out; to test that path, deploy the probe
# as a function and call it with public database URLs.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
IMAGE="${IMAGE:-blocks-functions-node:24-v2}"
NET="${NET:-drvnet}"
ip() { docker inspect "$1" --format "{{(index .NetworkSettings.Networks \"$NET\").IPAddress}}" 2>/dev/null; }
MONGO_URL="${MONGO_URL:-mongodb://$(ip drv-mongo):27017}"
REDIS_URL="${REDIS_URL:-redis://$(ip drv-redis):6379}"
PG_URL="${PG_URL:-postgres://postgres:test@$(ip drv-pg):5432/postgres}"
HTTP_URL="${HTTP_URL:-http://$(ip drv-mongo):27017}"
CASES=(fetch mongodb redis postgres crypto tmp childProcess workerThreads bcrypt)

[ -d "$HERE/node_modules" ] || { echo "run 'npm install --omit=dev' in $HERE first" >&2; exit 1; }
chmod -R a+rX "$HERE"

envelope() {
  printf '{"run":{"id":"%s"},"input":{"case":"%s"},"limits":{"timeoutMs":25000},"env":{"MONGO_URL":"%s","REDIS_URL":"%s","PG_URL":"%s","HTTP_URL":"%s"}}' \
    "$1" "$2" "$MONGO_URL" "$REDIS_URL" "$PG_URL" "$HTTP_URL"
}
sandbox() {
  docker run "$@" --rm --runtime=runsc --network="$NET" --cpus=0.1 --memory=128m --pids-limit=64 \
    --read-only --tmpfs /tmp:rw,noexec,nosuid,nodev,size=64m --cap-drop=ALL \
    --security-opt no-new-privileges --user 10001:10001 -v "$HERE":/function:ro "$IMAGE"
}

echo "== single-run mode: one cold sandbox per case"
for c in "${CASES[@]}"; do
  dir="$(mktemp -d)"; envelope "s-$c" "$c" > "$dir/execution.json"; chmod -R a+rX "$dir"
  start=$(( $(date +%s%N) / 1000000 ))
  line="$(sandbox -v "$dir":/run/blocks:ro 2>&1 | grep '"t":"result"' || true)"
  echo "$(( $(date +%s%N) / 1000000 - start )) ms wall  $line" | cut -c1-200
  rm -rf "$dir"
done

echo "== reuse mode: all cases, twice, in ONE sandbox"
{ for round in 1 2; do for c in "${CASES[@]}"; do envelope "r$round-$c" "$c"; echo; sleep 1; done; done; sleep 3; } \
  | sandbox -i -e BLOCKS_RUNTIME_MODE=reuse 2>&1 | grep -E '"t":"(ready|result|idle|fatal)"' | cut -c1-200

# Local test containers used by default:
#   docker network create drvnet
#   docker run -d --name drv-mongo --network drvnet mongo:7
#   docker run -d --name drv-redis --network drvnet redis:7-alpine
#   docker run -d --name drv-pg    --network drvnet -e POSTGRES_PASSWORD=test postgres:16-alpine

#!/usr/bin/env bash
# sandbox-profile.sh — the canonical `docker run` profile for a tenant function container,
# in shell form. Blocks.FunctionRunner applies the same profile through the Engine API; this
# file is what the pre-runner tooling (sample.sh) uses so the two cannot drift apart before
# A-3 replaces it.
#
# verify/verify.sh deliberately does NOT source this: a checker that shares its expectations
# with the thing it checks proves nothing. It restates every value independently.

: "${FN_NETWORK:=blocks-fn-egress}"
: "${FN_RESOLV_CONF:=/etc/blocks-runner/resolv.conf}"
: "${FN_CPUS:=0.2}"                  # 200 millicores
: "${FN_MEMORY:=300m}"               # 300 MB, swap equal
: "${FN_PIDS:=64}"
: "${FN_TMPFS_SIZE:=64m}"
: "${FN_UID:=10001}"

# sandbox_args <run-dir> — prints the docker arguments for one execution.
# <run-dir> must contain execution.json; it is mounted read-only at the envelope path.
sandbox_args() {
  local run_dir="$1"
  printf '%s\n' \
    --runtime=runsc \
    --network="$FN_NETWORK" \
    --cpus="$FN_CPUS" \
    --memory="$FN_MEMORY" \
    --memory-swap="$FN_MEMORY" \
    --pids-limit="$FN_PIDS" \
    --read-only \
    --tmpfs "/tmp:rw,noexec,nosuid,nodev,size=$FN_TMPFS_SIZE" \
    --cap-drop=ALL \
    --security-opt no-new-privileges \
    --user "$FN_UID:$FN_UID" \
    --volume "$run_dir/execution.json:/run/blocks/execution.json:ro" \
    --volume "$FN_RESOLV_CONF:/etc/resolv.conf:ro" \
    --env NODE_ENV=production \
    --env BLOCKS_EXECUTION_FILE=/run/blocks/execution.json \
    --env BLOCKS_RUNTIME_VERSION=1
}

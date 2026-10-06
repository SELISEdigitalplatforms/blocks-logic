#!/usr/bin/env bash
# Throughput of one host: cold single-run sandboxes vs reused sandboxes, at the production limits
# (gVisor, 0.1 CPU, 128 MB). Run it on the VM size you want numbers for (e.g. 8 cores / 16 GB).
#
#   ./run.sh                      # defaults below
#   SECONDS_PER_TEST=60 COLD_PARALLEL=24 WARM_SANDBOXES=60 WORK_MS=20 ./run.sh
#
# cold: COLD_PARALLEL loops, each starting a fresh sandbox per call (today's model).
# warm: WARM_SANDBOXES reused sandboxes, each fed calls back to back (reuse mode).
# Prints calls/s and latency percentiles, and host CPU use during each test.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
IMAGE="${IMAGE:-blocks-functions-node:24-v2}"
DURATION="${SECONDS_PER_TEST:-30}"
COLD_PARALLEL="${COLD_PARALLEL:-$(( $(nproc) * 2 ))}"
WARM_SANDBOXES="${WARM_SANDBOXES:-$(( $(nproc) * 8 ))}"
WORK_MS="${WORK_MS:-5}"
OUT="$(mktemp -d)"; trap 'rm -rf "$OUT"; docker rm -f $(docker ps -q --filter label=blocks-loadtest=1) >/dev/null 2>&1 || true' EXIT
mkdir -p "$OUT/fn" && cp "$HERE/fn.js" "$OUT/fn/index.js" && mkdir -p "$OUT/fn/node_modules" && chmod -R a+rX "$OUT"
LIMITS=(--runtime=runsc --network=none --cpus=0.1 --memory=128m --pids-limit=64 --read-only
        --tmpfs /tmp:rw,noexec,nosuid,nodev,size=64m --cap-drop=ALL --security-opt no-new-privileges
        --user 10001:10001 --label blocks-loadtest=1 -v "$OUT/fn":/function:ro)
now_ms() { echo $(( $(date +%s%N) / 1000000 )); }
cpu_busy() { awk '/^cpu /{print $2+$3+$4+$6+$7+$8, $2+$3+$4+$5+$6+$7+$8}' /proc/stat; }
report() { # $1 name, $2 file of latencies (ms), $3 wall seconds, $4 cpu%
  local sorted="$2.s"; sort -n "$2" > "$sorted"; n=$(wc -l < "$sorted")
  p() { [ "$n" -gt 0 ] && sed -n "$(( (n * $1 + 99) / 100 ))p" "$sorted" || echo -; }
  printf '%-5s calls=%-7s calls/s=%-8s p50=%sms p95=%sms p99=%sms host-cpu=%s%%\n' \
    "$1" "$n" "$(awk -v n="$n" -v s="$3" 'BEGIN{printf "%.1f", n/s}')" "$(p 50)" "$(p 95)" "$(p 99)" "$4"
}
envelope() { printf '{"run":{"id":"%s"},"input":{"workMs":%s},"limits":{"timeoutMs":30000}}' "$1" "$WORK_MS"; }

echo "host: $(nproc) cores, $(free -g | awk '/Mem:/{print $2}') GB; image $IMAGE; ${DURATION}s per test; work ${WORK_MS}ms/call"

# --- cold -------------------------------------------------------------------------------------
read b0 t0 < <(cpu_busy); start=$(now_ms); end=$(( start + DURATION * 1000 ))
for w in $(seq 1 "$COLD_PARALLEL"); do
  ( i=0; while [ "$(now_ms)" -lt "$end" ]; do
      i=$((i+1)); d="$OUT/c$w-$i"; mkdir -p "$d"; envelope "c$w-$i" > "$d/execution.json"; chmod -R a+rX "$d"
      s=$(now_ms); docker run --rm "${LIMITS[@]}" -v "$d":/run/blocks:ro "$IMAGE" >/dev/null 2>&1 || true
      echo $(( $(now_ms) - s )) >> "$OUT/cold.$w"; rm -rf "$d"
    done ) &
done; wait
read b1 t1 < <(cpu_busy); cat "$OUT"/cold.* > "$OUT/cold.all" 2>/dev/null || : > "$OUT/cold.all"
report cold "$OUT/cold.all" "$(( ( $(now_ms) - start ) / 1000 ))" "$(( 100 * (b1-b0) / (t1-t0) ))"

# --- warm -------------------------------------------------------------------------------------
cat > "$OUT/feed.py" <<'PY'
import subprocess, sys, time, json
args, dur, work, out = sys.argv[1:-3], float(sys.argv[-3]), int(sys.argv[-2]), sys.argv[-1]
p = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, bufsize=1)
while json.loads(p.stdout.readline()).get("t") != "ready": pass
end, i, lat = time.time() + dur, 0, []
while time.time() < end:
    i += 1; s = time.time()
    p.stdin.write(json.dumps({"run": {"id": f"w{i}"}, "input": {"workMs": work}, "limits": {"timeoutMs": 30000}}) + "\n"); p.stdin.flush()
    while json.loads(p.stdout.readline()).get("t") != "idle": pass
    lat.append(round((time.time() - s) * 1000))
p.stdin.close(); p.wait()
open(out, "w").write("\n".join(map(str, lat)) + ("\n" if lat else ""))
PY
read b0 t0 < <(cpu_busy); start=$(now_ms)
for w in $(seq 1 "$WARM_SANDBOXES"); do
  python3 "$OUT/feed.py" docker run -i --rm "${LIMITS[@]}" -e BLOCKS_RUNTIME_MODE=reuse "$IMAGE" \
    "$DURATION" "$WORK_MS" "$OUT/warm.$w" &
done; wait
read b1 t1 < <(cpu_busy); cat "$OUT"/warm.* > "$OUT/warm.all"
report warm "$OUT/warm.all" "$(( ( $(now_ms) - start ) / 1000 ))" "$(( 100 * (b1-b0) / (t1-t0) ))"

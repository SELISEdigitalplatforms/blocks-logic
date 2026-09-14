# blocks-functions-node:24-v1 — the base image every tenant function image is built FROM.
#
# It contributes the trusted runtime and the security-relevant image metadata; the
# per-execution ceilings (CPU, memory, PIDs, /tmp, network, capabilities) are applied by the
# runner at container-create time and enforced by gVisor and cgroups, not by anything here.
#
# The base image is pinned by digest so a rebuild is reproducible and a tag hijack cannot
# change what tenants run.

# node:24-bookworm-slim, Node v24.20.0 / npm 11.19.0
FROM node:24-bookworm-slim@sha256:ba849c60be29959425b8734d57b8b4b7d56f98edd9504c9af091d5281095a71e AS base

# Dedicated non-root identity. uid/gid 10001 is what the runner passes as --user, and what
# verify.sh asserts; naming it here means /function and /runtime ownership is meaningful
# even if the runtime flag were ever omitted.
RUN set -eux; \
    groupadd --gid 10001 blocksfn; \
    useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin blocksfn

# The trusted runtime: root-owned, world-readable, not writable by anyone. The rootfs is
# read-only at run time as well; this is the second lock on the same door.
COPY runtime/bootstrap.mjs runtime/protocol.mjs runtime/envelope.mjs /runtime/
RUN set -eux; \
    chown -R root:root /runtime; \
    chmod 0555 /runtime; \
    chmod 0444 /runtime/*.mjs; \
    mkdir -p /run/blocks; chown root:root /run/blocks; chmod 0555 /run/blocks

# Tenant code lives here; the builder copies into it and it stays root-owned, so a running
# function cannot rewrite its own source even if the rootfs were writable.
WORKDIR /function

# The complete OS environment a function may see (spec §19.2). The runner passes an
# allowlist that matches this and nothing else; verify.sh asserts no credentials leak in.
ENV NODE_ENV=production \
    BLOCKS_EXECUTION_FILE=/run/blocks/execution.json \
    BLOCKS_RUNTIME_VERSION=1 \
    NPM_CONFIG_UPDATE_NOTIFIER=false \
    NODE_OPTIONS=--max-old-space-size=225

LABEL org.opencontainers.image.title="Blocks Functions Node runtime" \
      org.opencontainers.image.version="24-v1" \
      dev.selise.blocks.runtime="node24" \
      dev.selise.blocks.runtime-version="1"

# ---------------------------------------------------------------------------------------
# Test target: the same runtime, plus the suite, run on the Node the runtime actually ships.
#   docker build --target test -t blocks-functions-node:24-v1-test .
#   docker run --rm --tmpfs /tmp:rw,exec,size=64m blocks-functions-node:24-v1-test
# /tmp needs exec here because node:test spawns the bootstrap from a temporary directory.
# ---------------------------------------------------------------------------------------
FROM base AS test
COPY runtime/bootstrap.test.mjs /runtime/
RUN chmod 0444 /runtime/bootstrap.test.mjs
USER 10001:10001
ENTRYPOINT ["node", "--test"]
CMD ["/runtime/bootstrap.test.mjs"]

# ---------------------------------------------------------------------------------------
# The shipping runtime. No test file, no shell in the entrypoint, no CMD to override.
# ---------------------------------------------------------------------------------------
FROM base AS runtime
USER 10001:10001
ENTRYPOINT ["node", "/runtime/bootstrap.mjs"]

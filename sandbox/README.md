# Blocks Functions — Runner VM

Executes tenant-supplied Node.js functions, one gVisor sandbox per invocation, on a dedicated VM.
Work arrives over Redis Streams and results go back the same way. This half never writes MongoDB
and holds no tenant database credentials.

The control plane — the API, the Worker, the Studio UI and the workflow node — is the rest of this
repository: `server/Functions.DomainService`, `server/Api`, `server/Worker` and
`client/app/modules/functions`. The two halves share a repository but nothing else: they are
separate deployables that only ever meet over Redis, and that contract is normative and documented
in `plan/PROTOCOL.md` of the design repo.

Nothing under `server/` builds this directory into an image — the Dockerfiles copy `server` and
`client` only, and `sandbox` is in `.dockerignore`. It ships by `deploy.sh` to a VM, and by
nothing else. Its four projects are listed in `server/Blocks.slnx` so CI builds and tests them
with the rest of the solution, and it keeps its own `src/Directory.Build.props` and
`src/Directory.Packages.props` — stricter analyzers and its own package pins, both explained in
those files.

## Layout

| Path | Holds |
|---|---|
| `provision/` | host setup, one numbered script per concern, idempotent |
| `runtime-image/` | the Node 24 image and the trusted runtime (`bootstrap.mjs`, `envelope.mjs`, `protocol.mjs`) |
| `src/` | `Blocks.FunctionRunner` (Genesis worker), `.Contracts` (the protocol as code), `.Cli` (`fnctl`), `.Tests` |
| `deploy/` | the systemd unit (installed by `make install`) and an env-file reference |

Verification and the sample functions live **outside this repository**, in `FunctionRunnerTests`,
so no test material can reach a VM — a plain sibling of `sandbox/` would now be inside the
repository, which is the one place it must not be. `make -C provision` looks for it beside
`blocks-logic`, then beside the repository root, then in `$HOME`, and takes the first it finds;
`make -C provision harness` prints which. Set `FUNCTION_RUNNER_TESTS` for anywhere else.

## Requirements

Ubuntu 20.04+ or Debian 10+, x86_64, kernel ≥ 5.10, ≥ 2 CPUs, ≥ 4 GB RAM, ≥ 20 GB free on
`/var`, cgroup v2, root. Outbound access to the distribution archive,
`storage.googleapis.com/gvisor`, Docker Hub, the npm registry and `api.nuget.org`, and —
because the runner's only job is to reach the Blocks Redis — the VPN. **No inbound access is
needed at all**: the runner listens on nothing, the image registry binds `127.0.0.1:5000`, and
work arrives because the runner reads Redis outbound. `provision/00-preflight.sh` enforces all
of it and refuses to let anything else run if a check fails.

Everything the provisioning needs is installed by the provisioning, including the tools it
runs on (`make`, `nftables`, `curl`, `tar`, `zstd`, `jq`, `rsync`): phase 1 bootstraps those
before it does anything else, so a minimal cloud image is a supported starting point. The two
things that vary by release are handled by picking a source, recorded in `.facts` as
`DOCKER_SOURCE` and `DOTNET_SOURCE`:

| Component | Source |
|---|---|
| Docker | the distro's `docker.io` when it is ≥ 20.10, otherwise `docker-ce` from `download.docker.com` (Ubuntu 20.04, Debian 10) |
| .NET 10 SDK | the distro archive (Ubuntu 26.04), else `packages.microsoft.com`, else `dotnet-install.sh` into `/usr/local/dotnet` |

Two requirements no package can supply, and both need a reboot — which is why preflight checks
them before installing anything and prints the fix:

- **cgroup v2.** Ubuntu switched in 21.10 and Debian in 11, so 20.04 and 10 boot the v1 hybrid
  hierarchy and need `systemd.unified_cgroup_hierarchy=1` in `GRUB_CMDLINE_LINUX`.
- **kernel ≥ 5.10**, for gVisor's systrap platform. Ubuntu 20.04 GA is 5.4 (use the HWE kernel)
  and Debian 10 is 4.19 (use backports).

The apt family is not a preference but the implementation: every script installs with `apt` and
names Debian's packages. Three escape hatches, all deliberate and all loud:

| Variable | Effect |
|---|---|
| `FN_ALLOW_UNTESTED_OS=1` | warn instead of refusing on anything but Ubuntu 20.04+ / Debian 10+ |
| `FN_ENDPOINT_PROBE=host:port` | probe this instead of what `runner.env` implies — the only way to check the VPN on a Key Vault host, which keeps no endpoint on disk |
| `FN_SKIP_ENDPOINT_PROBE=1` | do not check the Blocks endpoints at all (reconciling a host during an outage) |

## Deploy

One command takes a bare VM to a running runner, and is also how you redeploy:

```bash
sudo ./deploy.sh
```

Six phases: host provisioning, runtime image, configuration gate, build and test, install,
verification. Every phase is idempotent, so re-running is the supported way to reconcile a
host after editing `deny-cidrs`, `dns-servers`, `logging.conf` or `.versions`.

Idempotent here means nothing is restarted unless something changed, which is what makes this
safe to run from a pipeline on every commit:

| Restarted | Only when |
|---|---|
| Docker | `runsc` changed, `daemon.json` changed, or the Engine does not expose the `runsc` runtime yet |
| `blocks-fn-firewall` | the ruleset, the wrapper or the unit changed, or the `inet blocks_fn` table is not loaded |
| `blocks-function-runner` | the published binaries or the unit changed, or it is not running — compared by checksum, so a rebuild with identical output restarts nothing |

The Docker and firewall restarts matter more than they look: the firewall is `PartOf=docker.service`
and the runner `BindsTo` the firewall, so an unconditional restart in phase 1 would take the runner
down until phase 5 — including on deploys that then fail the configuration gate.

```bash
sudo ./deploy.sh --skip-tests   # faster redeploys, suites skipped
sudo make deploy                # the same thing
```

The one thing this repo cannot supply is `/etc/blocks-runner/runner.env`, the only
credentials on the VM. On a fresh host, phase 1 creates it from the template in
`provision/40-runner-user.sh` and phase 3 then stops the deploy until you have filled it in — deliberately, because a
runner started without real credentials does not crash, it sits in a retry loop looking
healthy. Fill it in and re-run the same command.

That stop is what makes a fresh host a two-pass deploy. When the credentials come from a
configuration system rather than a person, hand them over and it is one:

```bash
sudo ./deploy.sh --env-file /run/blocks-runner.env
```

Every `KEY=VALUE` in that file is merged into `runner.env` before the gate reads it — replacing
the commented placeholder where the template ships one, appending where it does not, and leaving
every other line alone. Values are never echoed, only key names. The file must be `chmod 600`,
because it holds exactly what `runner.env` holds and is usually left on the box afterwards.

`RUNNER__BaseImage` is the one line `deploy.sh` writes on its own, immediately after phase 2, and
it writes the *digest* of the image it just published rather than the tag — a tag moves under the
next `build.sh`, and what a tenant image is `FROM` must not. Point it at a registry this host does
not push to and `deploy.sh` leaves it alone and says so.

Phase 6 proves the runner actually works rather than merely starting: the unit is active
and its pid stable (not crash-looping), the egress firewall is loaded, the registry
answers, and — the one that matters — no `Heartbeat failed` appears in this invocation's
journal, which is the credential-free proof that the runner reached the Blocks Redis.

The individual phases stay available as `make provision|image|build|test|install` so one
can be repeated by hand during an incident.

Verification against the live host lives in the separate `FunctionRunnerTests` checkout and
is deliberately not part of `deploy.sh`, so no test material is required on a production
VM:

```bash
make -C provision verify FUNCTION_RUNNER_TESTS=/path/to/FunctionRunnerTests
```

## Operating

```bash
fnctl doctor                 # check this host against the contract
fnctl gc [--dry-run]         # prune unreferenced function images now
make status                  # unit state, runner heartbeats, stream depths
make logs                    # journalctl -f
```

There is no HTTP port and no metrics endpoint. Health is `fnctl doctor` plus the
`function:runner:{runnerId}` heartbeat hash in Redis.

Redis and MongoDB are the Blocks instances, reached over the VPN and resolved from Key
Vault (`BLOCKS_VAULT_TYPE=2`); nothing but the image registry runs on this host. Because a
Makefile cannot read Key Vault, the heartbeat and stream half of `make status` needs the
endpoint passed in:

```bash
BLOCKS_REDIS_URL='redis://:PASSWORD@REDIS-HOST:6379' make status
```

`fnctl doctor` has the same gap and its own variable, in StackExchange form. Without it the
Redis check fails against a loopback address nothing listens on any more:

```bash
FNCTL_REDIS='REDIS-HOST:6379,password=...' fnctl doctor
```

## Logs and disk

The runner writes to the journal and nowhere else on this host; container logs go to
Docker's `json-file` driver. Both, and the BuildKit cache, are bounded by
`/etc/blocks-runner/logging.conf`:

| Knob | Default | Bounds |
|---|---|---|
| `JOURNAL_MAX_RETENTION` | `1d` | how far back `journalctl` and `make logs` can go |
| `JOURNAL_MAX_USE` | `128M` | journal disk ceiling, enforced regardless of age |
| `DOCKER_LOG_MAX_SIZE` / `DOCKER_LOG_MAX_FILE` | `10m` / `3` | size budget per container |
| `DOCKER_BUILD_CACHE_MAX_PERCENT` | `2` | BuildKit cache ceiling, as a share of the disk behind `/var/lib/docker` (floored at 512 MB) |

Edit it and re-run `make -C provision logging`. That target owns the journal side; if you
change a `DOCKER_` value it will tell you to run `make -C provision docker`, which owns
`daemon.json` and restarts the Engine.

That covers everything on the host that grows: function images and registry manifests are
`ImageGc`'s job (`fnctl gc` runs a sweep by hand), container logs and the journal are the
table above, and the build cache is the last row — enforced by the daemon continuously, not
by a timer that would only run once the disk was already full.

Runs are queued by the control plane, not from here. To queue one by hand for a test, use
`FunctionRunnerTests/verify/verify.sh`'s `enqueue_run` helper.

## Security

See `SECURITY.md`. The short version: gVisor is mandatory and never falls back — for dependency
installs as well as runs, since `docker build` cannot be given a runtime — the sandbox gets no
credentials, and the design assumes this code is public.

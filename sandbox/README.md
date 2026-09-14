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
so no test material can reach a VM. Point `FUNCTION_RUNNER_TESTS` at your checkout; the default is
a directory of that name beside `blocks-logic` itself, because a plain sibling of `sandbox/` would
now be inside the repository, which is the one place it must not be.

## Requirements

x86_64, kernel ≥ 5.10, ≥ 2 CPUs, ≥ 4 GB RAM, ≥ 20 GB free on `/var`, cgroup v2, `nft`, root.
Outbound access to the Ubuntu archive, `storage.googleapis.com/gvisor`, Docker Hub, the npm
registry and `api.nuget.org`. `provision/00-preflight.sh` enforces all of it and refuses to let
anything else run if a check fails.

## Deploy

One command takes a bare VM to a running runner, and is also how you redeploy:

```bash
sudo ./deploy.sh
```

Six phases: host provisioning, runtime image, configuration gate, build and test, install,
verification. Every phase is idempotent, so re-running is the supported way to reconcile a
host after editing `deny-cidrs`, `dns-servers`, `logging.conf` or `.versions`. The service
is restarted only in phase 5 — after the configuration gate and the tests have passed — so
a bad build or a half-filled `runner.env` never takes down a runner that is working.

```bash
sudo ./deploy.sh --skip-tests   # faster redeploys, suites skipped
sudo make deploy                # the same thing
```

The one thing this repo cannot supply is `/etc/blocks-runner/runner.env`, the only
credentials on the VM. On a fresh host, phase 1 creates it from the template in
`provision/40-runner-user.sh` and phase 3 then stops the deploy until you have filled it in — deliberately, because a
runner started without real credentials does not crash, it sits in a retry loop looking
healthy. Fill it in and re-run the same command.

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

## Logs

The runner writes to the journal and nowhere else on this host; container logs go to
Docker's `json-file` driver. Both are bounded by `/etc/blocks-runner/logging.conf`:

| Knob | Default | Bounds |
|---|---|---|
| `JOURNAL_MAX_RETENTION` | `1d` | how far back `journalctl` and `make logs` can go |
| `JOURNAL_MAX_USE` | `128M` | journal disk ceiling, enforced regardless of age |
| `DOCKER_LOG_MAX_SIZE` / `DOCKER_LOG_MAX_FILE` | `10m` / `3` | size budget per container |

Edit it and re-run `make -C provision logging`. That target owns the journal side; if you
change a `DOCKER_` value it will tell you to run `make -C provision docker`, which owns
`daemon.json` and restarts the Engine.

Runs are queued by the control plane, not from here. To queue one by hand for a test, use
`FunctionRunnerTests/verify/verify.sh`'s `enqueue_run` helper.

## Security

See `SECURITY.md`. The short version: gVisor is mandatory and never falls back, the sandbox gets
no credentials, and the design assumes this code is public.

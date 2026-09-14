# Security model

Tenant code is untrusted and assumed hostile. This design does not rely on the code in this
repository being secret — everything below is enforced at runtime by the kernel, the sandbox and
the runner, not by obscurity.

## What confines a function

| Control | How |
|---|---|
| Kernel isolation | **gVisor (`runsc`) is mandatory**, for builds as well as runs. If it is missing the runner refuses all work and never falls back to `runc`. The runtime is compiled in, not read from configuration: a sandbox is created with the constant, and a host configured for anything else claims no work at all |
| Privileges | `--cap-drop=ALL`, `no-new-privileges`, non-root `uid 10001` |
| Filesystem | read-only root; `/tmp` is a `noexec,nosuid,nodev` tmpfs capped at 64 MB |
| Memory | `--memory` = `--memory-swap`, so there is no swap to escape into |
| Processes | PID ceiling 64 — note this counts **threads**, so a function gets far fewer than 64 processes |
| CPU / time | 200 millicores ceiling; a hard kill at `timeout + 2 s`, because the in-process deadline cannot preempt a synchronous busy loop |
| Mounts | exactly two: the read-only execution envelope and `resolv.conf`. **The container runtime socket is never mounted** |
| Environment | an allowlist, not a filter — nothing is inherited from the host |
| Network | public internet only. Denied: RFC1918, loopback, link-local, CGNAT, this VM's address **and its whole public /24**, plus IPv6 equivalents. Docker's embedded DNS does not work under gVisor, so a fixed `resolv.conf` is mounted instead — tenant lookups never touch the host resolver |
| Result / logs | 5 MB result, 1 MB / 10 000 log lines, enforced out of process by the runner |

Every limit a caller asks for is **re-clamped by the runner** before a sandbox is created, so a
mistake in the control plane cannot widen a sandbox.

## Builds are sandboxed too

`npm install` fetches, and with `allowScripts` executes, code the tenant chose. `docker build`
has no runtime selector — every RUN instruction lands on the Engine's default runtime, which is
deliberately `runc` — so the install does not happen during the image build. It happens first, in
its own gVisor sandbox (`BuildSandboxProfile`), and hands back `node_modules` as a tarball the
image build merely copies. Nothing in `function.Dockerfile.tmpl` runs tenant-chosen code, and
re-introducing a RUN that does would silently put it back on the host kernel.

The build sandbox carries the run profile unchanged — gVisor, `--cap-drop=ALL`,
`no-new-privileges`, uid 10001, read-only root, the confined egress network, an environment
allowlist — and differs only where a build must: one writable bind mount (the workspace, which
sits outside the build context), a 512 PID ceiling and a 512 MB `noexec` /tmp because npm forks
per package and unpacks through /tmp, and the build's own CPU, memory and time budget.

Two limits worth stating plainly:

- **`--ignore-scripts` is still the default**, and `DenyPrivateScriptsOnBuild=true` in
  `runner.env` refuses `allowScripts` builds outright on this host, whatever the control plane
  sent. It is off by default because the install is now confined.
- **Nothing here screens what a package *contains*.** Dependencies resolve fresh from
  `package.json` with no lockfile, and `SourceValidator` screens specifier *shape* — registry
  ranges only, no git/path/alias specifiers, a blocklist of host-reaching packages. A compromised
  version of a legitimate package is confined by the sandbox, not detected by it, and it still
  ships into the tenant's own function image.

## What a function never receives

- No bearer token. The envelope is built field by field from named properties, never by
  serialising the caller's context, and is **screened for credential-shaped keys** before it is
  written — a run fails rather than leaking one.
- No secrets. `{{secret.NAME}}` is resolved only in the control plane's output-action processor,
  after the run, outside the sandbox.
- No database or registry credentials. None exist on this VM except in `runner.env`.

## Blast radius if a sandbox escapes

Assume gVisor is defeated. The attacker gets this VM, and:

- The runner's user is in the `docker` group, which is **root-equivalent on this host**. Accepted
  deliberately: driving the engine is the runner's whole job and nothing else runs here.
- The only credentials present are in `/etc/blocks-runner/runner.env` — Redis, and the runner's
  own MongoDB for logs and configuration.
- **No tenant database credential is ever resident.** The runner returns results over Redis and
  the control-plane Worker is the single MongoDB writer. An escape reaches the queue, not tenant
  data.

That boundary is the reason the VM is single-purpose. Do not co-locate anything else on it.

## Operator responsibilities

1. **Never point `RUNNER__Runtime` at anything but `runsc`.** It is bound from Genesis
   configuration — `runner.env` *and* the `blocks-secret-function-runner` document, which lives
   off this VM. The profile ignores it and `deploy.sh`, `fnctl doctor` and the startup guard all
   refuse a host that sets it otherwise, but a host set that way simply stops taking work.
2. **Keep gVisor current.** The pinned release and its checksum are in `provision/.versions`, and
   the pin is public — that is the point of a checksum, but it also means an attacker knows which
   version to look up. Update it deliberately.
3. **Add your internal ranges** to `/etc/blocks-runner/deny-cidrs` and re-run
   `provision/30-network.sh`. The mandatory ranges are compiled in; a VPN is not.
4. **Never set `FN_ALLOW_HOST_SUBNET=1`** unless you have a specific reason — it permits sandboxes
   to reach this VM's neighbours.
5. **Protect `runner.env`** (0640 `root:blocks-runner`). It is the only secret on the host.
6. Do not weaken the sandbox profile in `runtime-image/sandbox-profile.sh`. It is asserted by
   `FunctionRunnerTests/verify/verify.sh`; run it after any change.

## Reporting

Report suspected sandbox escapes, network policy bypasses or credential exposure through the
internal security channel, not a public issue.

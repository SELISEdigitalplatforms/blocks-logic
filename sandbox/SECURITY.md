# Security model

Tenant code is untrusted and assumed hostile. This design does not rely on the code in this
repository being secret — everything below is enforced at runtime by the kernel, the sandbox and
the runner, not by obscurity.

## What confines a function

| Control | How |
|---|---|
| Kernel isolation | **gVisor (`runsc`) is mandatory.** If it is missing the runner refuses all work and never falls back to `runc` |
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

1. **Keep gVisor current.** The pinned release and its checksum are in `provision/.versions`, and
   the pin is public — that is the point of a checksum, but it also means an attacker knows which
   version to look up. Update it deliberately.
2. **Add your internal ranges** to `/etc/blocks-runner/deny-cidrs` and re-run
   `provision/30-network.sh`. The mandatory ranges are compiled in; a VPN is not.
3. **Never set `FN_ALLOW_HOST_SUBNET=1`** unless you have a specific reason — it permits sandboxes
   to reach this VM's neighbours.
4. **Protect `runner.env`** (0640 `root:blocks-runner`). It is the only secret on the host.
5. Do not weaken the sandbox profile in `runtime-image/sandbox-profile.sh`. It is asserted by
   `FunctionRunnerTests/verify/verify.sh`; run it after any change.

## Reporting

Report suspected sandbox escapes, network policy bypasses or credential exposure through the
internal security channel, not a public issue.

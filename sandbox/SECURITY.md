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
| Memory | 200 MB, with `--memory` = `--memory-swap`, so there is no swap to escape into. A request below 32 MB is raised to 32 MB: under Docker's own minimum a sandbox cannot even be created |
| Processes | PID ceiling 64 — note this counts **threads**, and under `runsc` gVisor's own host-side processes as well, so a function gets far fewer than 64 processes. A request below 32 is raised to 32, the measured floor at which a gVisor sandbox starts at all |
| CPU / time | 100 millicores, a fixed allocation rather than a ceiling a function may choose under; 90 s wall clock with a hard kill at `timeout + 2 s`, because the in-process deadline cannot preempt a synchronous busy loop |
| Mounts | exactly two: the read-only execution envelope and `resolv.conf`. **The container runtime socket is never mounted**. On the runner's disk the envelope is `0440`, group-owned by the sandbox's gid 10001, in a `0700` per-run directory, and it is deleted when the run ends, however it ends |
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

- Never the caller's own bearer token, and never anything credential-shaped from the queue. The
  envelope is built field by field from named properties, never by serialising the caller's
  context, and everything the platform writes into it (`run`, `context`, `limits`) is **screened
  for credential-shaped keys** before it is written — a run fails rather than leaking one. Two
  subtrees are exempt: `env`, whose keys are the tenant's own variable names, and `input`, which
  is the caller's own payload (for an HTTP trigger, its body and query) — a form posting a
  `password` field is ordinary input. The one token a function can receive is a fresh delegated
  one, added by the runner after that screen; see below.
- No platform credentials. No database, registry, IAM, Key Vault or Redis credential ever enters
  an envelope; none exist on this VM except in `runner.env`.
- No part in output actions. Output actions are performed by the control-plane Worker after the
  run, not by the sandbox, and nothing about them is in the envelope. Their targets are checked
  against private address space before the Worker calls them (a guard being added in the Worker).

## What a function does receive

**A delegated access token for its caller, as `ctx.blocks.accessToken`.** So a function can call
Blocks APIs (IAM, Data, Mail, Notifier) as the user who invoked it. It works like a message
worker's delegated access, with the same Genesis grant and the same IAM exchange:

- At invoke time, while the caller's validated token is in scope, the control plane writes a
  Genesis delegation grant (`FunctionDelegationService`) — a Redis record of tenant, user,
  organization, `token_version` and `security_stamp`, with a 2-hour absolute TTL — and the run
  hash carries only its opaque id, in its own `delegation` field beside the envelope. Not the
  caller's token, and not inside the envelope.
- Right before the sandbox starts, for **every attempt**, the runner redeems the grant with IAM
  (RFC 8693, signed with the tenant's salt — the id alone is useless) and gets a freshly minted
  token with IAM's normal access-token lifetime. IAM re-reads the user each time and refuses a
  deactivated user or one whose sessions were revoked since the grant was written.
- The runner adds it as `blocks.accessToken` **after** screening the queued envelope, so a token
  that arrived through the queue still fails the run; the write-time screen exempts exactly that
  one top-level path, and only when the runner itself added it (`RunDelegation`). It is added to
  `maskedValues`, so it is masked in every log line and error the bootstrap writes — though, like
  a secret-bound variable, not in a successful return value, which is the author's own output.
- Only an authenticated user on a non-public trigger, in the run's own tenant, and not under
  impersonation. A public call, a schedule, a `client_credentials` caller, or a grant IAM will
  not redeem: `ctx.blocks.accessToken` is `undefined` and the run goes on. A token is never a
  reason a run fails.

The consequence is the same as for a bound secret: during the run, the function's code holds a
working token for its caller and can send it anywhere on the public internet. It is that caller's
own identity — no more than the caller could do themselves — and it expires on IAM's schedule.


**The tenant's own secrets, when the tenant binds them to a variable.** A variable whose value
references `{{secret.<id>}}` (whole, or embedded as in `Bearer {{secret.<id>}}`) is **resolved by
the runner**, not the control plane. The control plane enqueues the envelope with the reference
left in `env` and the key listed in `maskedEnv` (`FunctionEnvelopeBuilder.BuildEnv`); the run entry
carries protocol 2. Right before the sandbox starts, the runner resolves every reference of the run
in one lookup for the run's tenant (`Runs/EnvSecretReferences`, `SecretStore/BlocksSecretsRunResolver`,
through `SeliseBlocks.Secrets.OS`), substitutes the values into `env`, and the function reads the
real value as `ctx.env.NAME`. That is the feature — a function that calls Stripe needs the Stripe
key — and it has consequences worth stating:

- The function can do anything with the value, including send it anywhere on the public
  internet. Binding a secret to a function is trusting that function's code with it.
- **Where the plaintext exists**: in the runner process's memory while the run starts, and in the
  per-run envelope file on this VM until the run ends — nowhere else. Not in Redis (the run record
  and its retry copy hold only references; a retry resolves them again, so a rotated secret takes
  effect on the next attempt), not in MongoDB, not in any log. The file is 0440, owned by the
  runner with the sandbox's group (gid 10001), and deleted on every exit path; if the runner
  process itself dies mid-run, the file stays until the reaper clears orphaned run directories, a
  few minutes later.
- **Whose secrets**: the runner reads as the run's tenant, with the caller identity the control
  plane recorded in the envelope (user, organisation, roles), authenticated. So a `service` or
  `both` secret resolves for any run of that tenant, and an access-listed `api` secret only when
  the run's caller is on its list or created it. The entry's tenant and the envelope's must agree,
  or nothing is resolved. A reference in the caller's `input` is never resolved.
- **When it cannot be resolved** the sandbox is not started. A reference to a secret that does not
  exist, is locked or deleted, has no value, or is not readable fails the run as
  `SECRET_UNRESOLVED` (not retried) with a message naming the variable key and the secret id —
  never a value. A secret store that cannot be reached (Key Vault, the tenant registry or the
  tenant's database, or no answer within 20 s) fails it as `SECRET_STORE_UNAVAILABLE`, which the
  control plane retries.
- The runtime masks secret-backed values out of every log line and error it writes (not a
  successful return value — returning a secret is the function's deliberate choice): the
  runner hands the bootstrap `maskedEnv` (the keys) and `maskedValues` (each bare value, so the
  token inside `Bearer <token>` is masked on its own). The runner applies the same rule to the
  error message it forwards and never logs an envelope or an env value. Masking guards against
  accidents, not against the function: a value transformed before it is logged (encoded, split,
  reversed) is not recognised.

## Blast radius if a sandbox escapes

Assume gVisor is defeated. The attacker gets this VM, and:

- The runner's user is in the `docker` group, which is **root-equivalent on this host**. Accepted
  deliberately: driving the engine is the runner's whole job and nothing else runs here.
- The only credentials stored on this VM are in `/etc/blocks-runner/runner.env` — Redis, the
  runner's MongoDB connection (logs, configuration and the tenant registry), and the Key Vault
  service principal the runner resolves secrets with.
- **It reaches the tenants' secrets.** Resolving secrets at run start is this host's job, so an
  escape inherits what that needs: the tenant registry (and through it each tenant's database
  connection, where secret metadata lives) and read access to the Key Vault holding every
  tenant's secret values. Scope the service principal to `get` on secrets and nothing else, and
  treat an escape as a disclosure of every tenant secret in that vault — rotate them.
- **It reaches the queue, and the queue carries tenant data** — caller input for every run whose
  record is still in Redis, and the resolved envelopes of runs executing on this VM. It no longer
  carries secret values: those are resolved here, per run, and never written back.
- **It can write to the results stream.** The Worker's result consumer locates a run by the
  `tenantId` and `runId` on the entry, so a forged entry could misreport a run's outcome or result
  and so drive its output actions. The consumer is being hardened to check an entry against the
  run it claims to be rather than trusting those fields.

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
5. **Protect `runner.env`** (0640 `root:blocks-runner`). It is the only credential stored on the
   host — and it now includes the Key Vault principal that can read tenant secret values. Point
   `KeyVault__KeyVaultUrl` at the same vault as the control plane; without it the secrets SDK
   falls back to its Database store and every secret-bound run fails as `SECRET_UNRESOLVED`.
6. **Keep the runner in the sandbox's group** (gid 10001, created by `provision/40-runner-user.sh`).
   It is how the envelope reaches the sandbox without being world-readable; a runner outside it
   reports itself unhealthy and claims no runs.
7. Do not weaken the sandbox profile in `runtime-image/sandbox-profile.sh`. It is asserted by
   `FunctionRunnerTests/verify/verify.sh`; run it after any change.

## Reporting

Report suspected sandbox escapes, network policy bypasses or credential exposure through the
internal security channel, not a public issue.

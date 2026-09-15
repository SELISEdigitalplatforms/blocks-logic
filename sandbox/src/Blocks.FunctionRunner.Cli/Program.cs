using System.Globalization;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Maintenance;
using Blocks.FunctionRunner.Options;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

// fnctl — the operator's tool for the Runner VM.
//
//   fnctl doctor                                   check the host against the contract
//   fnctl gc [--dry-run] [--grace-hours N]         prune unreferenced function images now
//
// Deliberately only these two. `enqueue-run`, `tail-results` and `stats` existed while there
// was no control plane; blocks-logic owns enqueueing now (FunctionInvocationService), and a
// second writer of the run protocol is exactly the kind of thing that drifts from the first.
// What is left has no other home: `doctor` is the host-contract check the HANDOFF points at
// (the runner exposes no metrics port), and `gc` runs one sweep of the runner's own ImageGc so
// "prune now" and the scheduled hourly sweep cannot disagree about what is safe to delete.

var command = args.Length > 0 ? args[0] : "help";

// Where Redis is, in decreasing order of authority. There is deliberately no loopback
// default any more: nothing listens there since the runner moved to the Blocks instances
// over the VPN, so a default would only ever produce a confident, wrong "unreachable".
// null means "no endpoint is knowable from here" — a check that cannot run, not a failure.
var redisUrl = GetOption("--redis")
    ?? Environment.GetEnvironmentVariable("FNCTL_REDIS")
    ?? RedisFromRunnerEnv();

try
{
    return command switch
    {
        "doctor" => await DoctorAsync(),
        "gc" => await GcAsync(),
        "help" or "--help" or "-h" => Help(),
        _ => Fail($"unknown command '{command}'"),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"fnctl: {ex.Message}");
    return 1;
}

// ------------------------------------------------------------------- commands ----

async Task<int> DoctorAsync()
{
    var problems = 0;
    Console.WriteLine("Runner VM checks");

    // What this host is actually configured with, not what most hosts are. Hardcoding these
    // meant doctor reported a perfectly healthy non-default host as broken, and — worse — a
    // host whose runner.env pointed somewhere else as fine.
    var network = Setting("RUNNER__Network", "blocks-fn-egress");
    var runsDir = Setting("RUNNER__RunsDir", "/var/lib/blocks-runner/runs");
    var buildsDir = Setting("RUNNER__BuildsDir", "/var/lib/blocks-runner/builds");
    var resolvConf = Setting("RUNNER__ResolvConf", "/etc/blocks-runner/resolv.conf");

    // --- gVisor: the one that is not negotiable --------------------------------
    try
    {
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        await docker.System.PingAsync();
        Report(true, "docker engine reachable");

        var info = await docker.System.GetSystemInfoAsync();
        var hasRunsc = info.Runtimes?.ContainsKey(Ceilings.SandboxRuntime) == true;
        problems += Report(hasRunsc, hasRunsc
            ? "runsc runtime registered"
            : "runsc runtime MISSING — the runner will refuse all work");

        // Registered is not the same question as configured. RUNNER__Runtime is bound from
        // Genesis configuration, so a host can have gVisor installed and still be told to use
        // something else; asking only the first question would report that host as fine. Read
        // through Setting, not the environment alone: an operator's shell does not carry the
        // unit's environment, so a runner.env pointing at runc went unnoticed here.
        var configuredRuntime = Setting("RUNNER__Runtime", Ceilings.SandboxRuntime);
        var runtimeOk = configuredRuntime == Ceilings.SandboxRuntime;
        problems += Report(runtimeOk, runtimeOk
            ? $"configured runtime is {configuredRuntime}"
            : $"RUNNER__Runtime is '{configuredRuntime}', not '{Ceilings.SandboxRuntime}' — the runner will refuse all work");
        problems += Report(info.DefaultRuntime == "runc", $"default runtime is {info.DefaultRuntime}");
        problems += Report(info.CgroupVersion == "2", $"cgroup version {info.CgroupVersion}");

        var networks = await docker.Networks.ListNetworksAsync();
        var net = networks.FirstOrDefault(n => n.Name == network);
        problems += Report(net is not null, net is not null
            ? $"network {network} exists"
            : $"network {network} MISSING — run provision/30-network.sh");
    }
    catch (Exception ex)
    {
        problems += Report(false, $"docker engine unreachable: {ex.Message}");
    }

    // --- the pieces a sandbox cannot start without -----------------------------
    problems += Report(File.Exists(resolvConf), $"{resolvConf} present (sandbox DNS depends on it)");
    problems += Report(Directory.Exists(runsDir), $"{runsDir} present");
    // Builds fail the same way runs do without their directory, and nothing else checks it.
    problems += Report(Directory.Exists(buildsDir), $"{buildsDir} present");

    // --- Redis ------------------------------------------------------------------
    // On a Key Vault host (BLOCKS_VAULT_TYPE=2) the connection string is resolved inside
    // the runner and never touches disk, so fnctl cannot dial it. Say so and move on:
    // reporting a problem here would fail every healthy deploy. The runner's own view of
    // Redis is authoritative anyway, and `Heartbeat failed` in the journal is where it
    // surfaces — deploy.sh checks exactly that.
    if (string.IsNullOrWhiteSpace(redisUrl))
    {
        Console.WriteLine("  skip redis not checked — no endpoint here (Key Vault resolves it in the runner)");
        Console.WriteLine("       to check it: FNCTL_REDIS='host:6379,password=...' fnctl doctor");
    }
    else
    {
        try
        {
            await using var redis = await ConnectAsync();
            var db = redis.GetDatabase();
            await db.PingAsync();
            Report(true, $"redis reachable at {Redact(redisUrl)}");

            var server = redis.GetServer(redis.GetEndPoints().First());
            var aof = (await server.ConfigGetAsync("appendonly")).FirstOrDefault().Value;
            problems += Report(aof == "yes",
                aof == "yes" ? "redis appendonly on" : $"redis appendonly is '{aof}' — results may be lost on restart");
        }
        catch (Exception ex)
        {
            problems += Report(false, $"redis unreachable: {ex.Message}");
        }
    }

    Console.WriteLine();
    Console.WriteLine(problems == 0 ? "doctor: all checks passed" : $"doctor: {problems} problem(s)");
    return problems == 0 ? 0 : 1;
}

async Task<int> GcAsync()
{
    // Runs exactly one sweep of the runner's own ImageGc, so "prune now" and the scheduled
    // hourly sweep cannot disagree about what is safe to delete.
    var graceHours = double.Parse(GetOption("--grace-hours") ?? "6", CultureInfo.InvariantCulture);

    await using var redis = await ConnectAsync();
    using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();

    var candidates = await docker.Images.ListImagesAsync(new ImagesListParameters
    {
        All = false,
        Filters = new Dictionary<string, IDictionary<string, bool>>
        {
            ["label"] = new Dictionary<string, bool> { ["dev.selise.blocks.function=true"] = true },
        },
    });
    var keep = await redis.GetDatabase().SetMembersAsync(RedisKeys.ImagesKeep);

    Console.WriteLine(
        $"{candidates.Count} labelled function image(s); keep set holds {keep.Length} reference(s); grace {graceHours}h");

    if (HasFlag("--dry-run"))
    {
        var cutoff = DateTime.UtcNow.AddHours(-graceHours);
        foreach (var image in candidates)
        {
            var label = image.RepoTags?.FirstOrDefault() ?? image.RepoDigests?.FirstOrDefault() ?? image.ID;
            var young = image.Created > cutoff;
            Console.WriteLine($"  {label}  created={image.Created:u}{(young ? "  (inside grace)" : string.Empty)}");
        }
        Console.WriteLine("dry run: nothing was removed");
        return 0;
    }

    // The base image must be the one the runner is configured with, not a default that happens
    // to match on most hosts: it is the reference the sweep refuses to prune, and getting it
    // wrong on a host pointed at another registry means deleting the image every build is FROM.
    // The registry goes with it, because that is what decides whether manifests are deleted too.
    var options = Options.Create(new RunnerOptions
    {
        BaseImage = GetOption("--base-image")
            ?? Setting("RUNNER__BaseImage", "127.0.0.1:5000/blocks/functions-node:24-v1"),
        Registry = Setting("RUNNER__Registry", "127.0.0.1:5000"),
        // And the rest of what decides whether a manifest delete happens and succeeds. Left at
        // their defaults, `fnctl gc` on a shared-registry host would delete manifests the runner
        // deliberately leaves alone — the one mistake in this path that another host sees.
        RegistryTls = Flag("RUNNER__RegistryTls"),
        PruneRegistry = Flag("RUNNER__PruneRegistry"),
        RegistryUsername = Setting("RUNNER__RegistryUsername", string.Empty),
        RegistryPassword = Setting("RUNNER__RegistryPassword", string.Empty),
    });
    Console.WriteLine(
        $"base image: {options.Value.BaseImage}; registry {options.Value.Registry} " +
        $"({(options.Value.ShouldPruneRegistry ? "manifests are deleted" : "manifests are left alone")})");

    // A one-off HttpClient, not AddHttpClient<>: fnctl is a standalone console tool with no DI
    // container of its own (unlike the runner, see ApplicationServiceCollectionExtensions).
    using var registryHttp = new HttpClient();
    var registry = new RegistryClient(registryHttp, options, NullLogger<RegistryClient>.Instance);

    using var gc = new ImageGc(
        docker,
        redis.GetDatabase(),
        registry,
        options,
        NullLogger<ImageGc>.Instance,
        TimeSpan.FromHours(graceHours));

    var removed = await gc.SweepAsync(CancellationToken.None);
    Console.WriteLine($"pruned {removed} image(s)");
    return 0;
}

// -------------------------------------------------------------------- helpers ----

async Task<IConnectionMultiplexer> ConnectAsync()
{
    if (string.IsNullOrWhiteSpace(redisUrl))
    {
        throw new InvalidOperationException(
            "no Redis endpoint: set FNCTL_REDIS or pass --redis (Key Vault hosts do not expose it on disk)");
    }

    var config = ConfigurationOptions.Parse(redisUrl);
    config.AllowAdmin = true;
    config.ConnectTimeout = 5000;
    return await ConnectionMultiplexer.ConnectAsync(config);
}

int Report(bool ok, string message)
{
    Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {message}");
    return ok ? 0 : 1;
}

string? GetOption(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

bool HasFlag(string name) => Array.IndexOf(args, name) >= 0;

// A tri-state runner setting: null means "not set", which is what RunnerOptions reads as
// "decide from the registry address". An unparseable value is treated as unset rather than as
// false, so a typo cannot quietly turn registry pruning off (or on).
static bool? Flag(string key)
{
    var raw = Environment.GetEnvironmentVariable(key);
    if (string.IsNullOrWhiteSpace(raw)) raw = RunnerEnvValue(key);
    return bool.TryParse(raw, out var value) ? value : null;
}

/// <summary>
/// One value out of the runner's env file.
/// <para>
/// fnctl is normally run from an operator's shell, not from the unit, so <c>RUNNER__*</c> is
/// almost never in this process's environment — the file is where the host's real configuration
/// is. Reading it is what makes doctor check <i>this host</i> rather than a set of defaults that
/// happen to be right on most of them.
/// </para>
/// </summary>
static string? RunnerEnvValue(string key)
{
    const string path = "/etc/blocks-runner/runner.env";
    try
    {
        if (!File.Exists(path)) return null;
        string? found = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (!line.StartsWith(key + "=", StringComparison.Ordinal)) continue;
            var value = line[(key.Length + 1)..].Trim().Trim('"', '\'');
            // Last one wins, as it does for systemd's EnvironmentFile.
            found = value.Length == 0 ? null : value;
        }
        return found;
    }
    catch (IOException) { /* unreadable is the same as absent for this purpose */ }
    catch (UnauthorizedAccessException) { /* fnctl run as a non-root operator */ }

    return null;
}

/// <summary>
/// A runner setting, in the order the runner itself resolves one: the process environment first
/// (which is what the unit has), then the env file, then the built-in default.
/// </summary>
static string Setting(string key, string fallback)
{
    var fromEnvironment = Environment.GetEnvironmentVariable(key);
    if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;
    return RunnerEnvValue(key) ?? fallback;
}

// OnPrem hosts (BLOCKS_VAULT_TYPE=1) keep the connection string in the runner's env file,
// so an operator on the box needs no extra configuration. Key Vault hosts have nothing
// here to find, and that is the case that must return null rather than guessing.
static string? RedisFromRunnerEnv() => RunnerEnvValue("BlocksSecret__CacheConnectionString");

// The endpoint is printed on a terminal an operator may be sharing; the password is not.
static string Redact(string connection) => string.Join(',', connection
    .Split(',')
    .Select(part => part.TrimStart().StartsWith("password=", StringComparison.OrdinalIgnoreCase)
        ? "password=***"
        : part));

static int Help()
{
    Console.WriteLine("""
        fnctl — Blocks Functions runner control

          doctor                                     check this host against the contract
          gc [--dry-run] [--grace-hours N]           prune unreferenced function images now
              --dry-run           list candidates without removing anything
              --grace-hours N     protect images younger than N hours (default 6)
              --base-image REF    the base image to keep (default the local registry's)

        --redis HOST:PORT overrides FNCTL_REDIS, which overrides
        BlocksSecret__CacheConnectionString in /etc/blocks-runner/runner.env. On a Key
        Vault host none of these are set, so Redis-backed checks report "skip".

        Runs are queued by blocks-logic, not from here. To queue one by hand for a test, see
        verify/verify.sh's enqueue_run helper, which XADDs to functions:runs directly.
        """);
    return 0;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"fnctl: {message}");
    return 2;
}

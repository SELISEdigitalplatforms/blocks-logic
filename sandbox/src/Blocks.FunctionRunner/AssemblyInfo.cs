using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

// The runner is Linux-only and not conditionally so: gVisor is mandatory, the sandbox
// profile is expressed in cgroup and Unix file-mode terms, and the service ships as a
// systemd unit. Declaring it here makes the platform APIs below legal instead of guarded
// by branches that could never run.
[assembly: SupportedOSPlatform("linux")]

[assembly: InternalsVisibleTo("Blocks.FunctionRunner.Tests")]
[assembly: InternalsVisibleTo("fnctl")]

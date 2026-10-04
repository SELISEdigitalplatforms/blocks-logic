using System.Runtime.Versioning;
using Xunit;

// The runner is Linux-only; so is its test suite.
[assembly: SupportedOSPlatform("linux")]

// Redis-backed admission keys are process-wide on the shared CI Redis. Parallel test
// classes racing HostBudget/TenantSlots flake under Sonar's full suite; keep the
// assembly serial so RunProcessor/FunctionConcurrency tests never share slots.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

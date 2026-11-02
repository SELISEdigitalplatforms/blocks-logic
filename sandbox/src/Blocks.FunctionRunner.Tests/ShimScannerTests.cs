using Blocks.FunctionRunner.Health;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Which containerd shims police may end. A wrong answer here kills a live tenant's
    /// container, so the rule is tested on both sides: never one Docker lists, never a young one.
    /// </summary>
    public sealed class ShimScannerTests
    {
        private static readonly DateTimeOffset Noon = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        private static readonly string IdA = new('a', 64);
        private static readonly string IdB = new('b', 64);
        private static readonly string IdC = new('c', 64);

        [Fact]
        public void The_container_id_is_read_from_a_shim_command_line()
        {
            var cmdline = $"/usr/bin/containerd-shim-runc-v2\0-namespace\0moby\0-id\0{IdA}\0-address\0/run/containerd/containerd.sock\0";

            ShimScanner.TryParseCommandLine(cmdline, out var id).Should().BeTrue();
            id.Should().Be(IdA);
        }

        [Fact]
        public void Other_processes_are_not_shims_and_a_shim_without_a_valid_id_has_none()
        {
            ShimScanner.TryParseCommandLine("/usr/bin/dockerd\0-id\0" + IdA, out _).Should().BeFalse();

            ShimScanner.TryParseCommandLine("/usr/bin/containerd-shim-runc-v2\0-id\0not-an-id\0", out var id).Should().BeTrue();
            id.Should().BeNull();
        }

        [Fact]
        public void Only_old_shims_without_a_docker_container_are_orphans()
        {
            var old = Noon.AddMinutes(-30);
            var shims = new[]
            {
                new ShimProcess(1, IdA, old),                  // Docker lists it: never touched.
                new ShimProcess(2, IdB, old),                  // orphan.
                new ShimProcess(3, IdC, Noon.AddSeconds(-30)), // orphan but too young.
                new ShimProcess(4, null, old),                 // id unreadable: never touched.
            };

            var orphans = ShimScanner.Orphans(shims, new HashSet<string> { IdA }, Noon, HealthThresholds.PoliceOrphanMinAge);

            orphans.Select(s => s.Pid).Should().Equal(2);
        }

        [Fact]
        public void A_shim_that_docker_lists_as_exited_or_paused_is_not_an_orphan()
        {
            // Docker's All=true list includes stopped and paused containers, so the caller's id set does too.
            var shims = new[] { new ShimProcess(1, IdA, Noon.AddHours(-3)) };

            ShimScanner.Orphans(shims, new HashSet<string> { IdA }, Noon, TimeSpan.Zero).Should().BeEmpty();
        }

        [Fact]
        public void Start_time_comes_from_field_22_even_when_the_command_name_has_spaces_and_brackets()
        {
            var boot = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
            // fields after ')': state ppid pgrp session tty tpgid flags minflt cminflt majflt cmajflt utime stime
            // cutime cstime priority nice threads itrealvalue starttime(=6000 ticks)
            var stat = "123 (containerd-shim (x) y) S 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 6000 99 100";

            ShimScanner.ParseStartTime(stat, boot).Should().Be(boot.AddSeconds(60));
        }
    }
}

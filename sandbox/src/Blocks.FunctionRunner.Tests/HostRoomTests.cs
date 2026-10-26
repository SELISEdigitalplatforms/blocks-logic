using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// The live room check (RUNNER-FLOW step 0). On 2026-10-26 the old counting budget refused every
    /// run on an idle machine because a count was never given back; only a restart cleared it. These
    /// tests hold the rules that replace it: decisions come from the machine as read now, and the one
    /// bounded limit (3 cold starts) frees itself.
    /// </summary>
    public sealed class HostRoomTests
    {
        private const long Mb = 1024L * 1024;
        private readonly ManualTime _time = new();

        private HostRoom Room(CalmHost host, Action<RunnerOptions>? configure = null)
        {
            var options = new RunnerOptions();
            configure?.Invoke(options);
            return new HostRoom(Microsoft.Extensions.Options.Options.Create(options), host, NullLogger<HostRoom>.Instance, _time);
        }

        [Theory]
        [InlineData(2, 1)]
        [InlineData(4, 1)]
        [InlineData(8, 1.5)]
        [InlineData(16, 2)]
        public void The_reserve_follows_the_machine_size_and_never_exceeds_two_cores(double cores, double reserved)
        {
            HostRoom.ReservedCores(cores, configured: null).Should().Be(reserved);
        }

        [Fact]
        public void A_configured_reserve_wins_but_stays_between_one_and_two_cores()
        {
            HostRoom.ReservedCores(16, configured: 1).Should().Be(1);
            HostRoom.ReservedCores(16, configured: 5).Should().Be(2);
        }

        [Fact]
        public void An_idle_machine_has_room_and_a_resized_one_is_read_again_without_a_restart()
        {
            var host = new CalmHost { Cores = 8 };
            var room = Room(host);
            room.Read().SandboxCores.Should().Be(6.5);

            host.Cores = 16;

            room.Read().SandboxCores.Should().Be(14);
            room.TryStartCold(Ceilings.MemoryBytes, out _).Should().NotBeNull();
        }

        [Fact]
        public void A_busy_machine_refuses_a_start_but_still_takes_a_warm_call()
        {
            // 8 cores, 1.5 reserved: 6.5 for sandboxes. 7.8 busy leaves 0.2 — enough for the run CPU, not the start boost.
            var room = Room(new CalmHost { Cores = 8, BusyCores = 7.8 });

            room.TryStartCold(Ceilings.MemoryBytes, out var refusal).Should().BeNull();
            refusal.Should().Be(RoomRefusal.Cpu);
            room.HasRoomForWarmCall().Should().BeTrue();
            room.LastRefusal.Should().BeEmpty("the last check said yes");
        }

        [Fact]
        public void The_cpu_the_reserve_uses_does_not_take_room_from_sandboxes()
        {
            // A build busy on the reserved 1.5 cores: all 6.5 sandbox cores are still free.
            var room = Room(new CalmHost { Cores = 8, BusyCores = 1.5 });

            room.Read().SandboxCpuFree.Should().Be(6.5);
        }

        [Fact]
        public void A_start_needs_its_ram_above_the_two_gb_reserve()
        {
            var room = Room(new CalmHost { AvailableMemoryBytes = (2048 + 100) * Mb });

            room.TryStartCold(Ceilings.MemoryBytes, out var refusal).Should().BeNull();
            refusal.Should().Be(RoomRefusal.Memory);
            room.LastRefusal.Should().Contain("no RAM for a start");
        }

        [Fact]
        public void At_most_three_cold_starts_at_once_and_a_released_one_frees_its_place()
        {
            var room = Room(new CalmHost());
            var starts = Enumerable.Range(0, 3).Select(i => room.TryStartCold(Ceilings.MemoryBytes, out _)).ToList();
            starts.Should().OnlyContain(s => s != null);

            room.TryStartCold(Ceilings.MemoryBytes, out var refusal).Should().BeNull();
            refusal.Should().Be(RoomRefusal.ColdStarts);

            starts[0]!.Dispose();
            room.TryStartCold(Ceilings.MemoryBytes, out _).Should().NotBeNull();
        }

        /// <summary>A start in flight is not in /proc yet: its CPU and RAM must not be handed out twice.</summary>
        [Fact]
        public void Starts_in_flight_take_their_cpu_and_ram_before_the_next_start()
        {
            // 6.5 sandbox cores, 1.8 free: room for one 1-core start, not for a second while it is in flight.
            var cpu = Room(new CalmHost { Cores = 8, BusyCores = 6.2 });
            cpu.TryStartCold(Ceilings.MemoryBytes, out _).Should().NotBeNull();
            cpu.TryStartCold(Ceilings.MemoryBytes, out var cpuRefusal).Should().BeNull();
            cpuRefusal.Should().Be(RoomRefusal.Cpu);

            // RAM for one sandbox above the 2 GB reserve, not for two.
            var ram = Room(new CalmHost { AvailableMemoryBytes = (2048 * Mb) + Ceilings.MemoryBytes + (64 * Mb) });
            ram.TryStartCold(Ceilings.MemoryBytes, out _).Should().NotBeNull();
            ram.TryStartCold(Ceilings.MemoryBytes, out var ramRefusal).Should().BeNull();
            ramRefusal.Should().Be(RoomRefusal.Memory);
        }

        [Fact]
        public void A_cold_start_never_released_stops_counting_after_the_boost_window()
        {
            var room = Room(new CalmHost());
            for (var i = 0; i < HostRoom.MaxColdStarts; i++) room.TryStartCold(Ceilings.MemoryBytes, out _);
            room.HasRoomForStart(Ceilings.MemoryBytes).Should().BeFalse();

            _time.Advance(TimeSpan.FromSeconds(5));

            room.HasRoomForStart(Ceilings.MemoryBytes).Should().BeTrue("a leaked start frees itself; nothing waits for a restart");
            room.Read().ColdStarts.Should().Be(0);
        }

        [Fact]
        public void A_build_needs_ram_only()
        {
            // No sandbox CPU free at all: a build still fits, it runs on the reserved cores.
            new HostRoom(Microsoft.Extensions.Options.Options.Create(new RunnerOptions()),
                new CalmHost { BusyCores = 8, AvailableMemoryBytes = 4096 * Mb }, NullLogger<HostRoom>.Instance)
                .HasRoomForBuild().Should().BeTrue();

            // 2 GB for the build plus the 2 GB reserve, and 1 MB short.
            var tight = Room(new CalmHost { AvailableMemoryBytes = (4096 - 1) * Mb });
            tight.HasRoomForBuild().Should().BeFalse();
            tight.BuildMemoryShortBytes().Should().Be(Mb);
        }

        [Fact]
        public void A_test_waits_while_less_than_30_percent_of_the_sandbox_cpu_is_free()
        {
            // 6.5 sandbox cores: 30 % is 1.95.
            Room(new CalmHost { BusyCores = 1.5 + 4.6 }).HasRoomForTest().Should().BeFalse();
            Room(new CalmHost { BusyCores = 1.5 + 4.5 }).HasRoomForTest().Should().BeTrue();
        }
    }
}

using System.Globalization;
using Blocks.FunctionRunner.Health;

namespace Blocks.FunctionRunner.Cli.Commands
{
    /// <summary>
    /// <c>fnctl stats</c>, <c>stats --hour</c>, <c>events</c> and <c>watch</c>: read what the
    /// runner's recorder wrote under its metrics directory. Nothing here talks to the runner, so
    /// they still work (on the last data) when it is down.
    /// </summary>
    internal static class MetricsCommands
    {
        private static readonly TimeSpan WatchEvery = TimeSpan.FromSeconds(2);

        public static int Stats(string[] args, string dir)
        {
            var store = new MetricsStore(dir, TimeProvider.System, createDirectory: false);
            if (CliArgs.Flag(args, "--hour"))
            {
                var samples = store.ReadSamples(TimeSpan.FromHours(1));
                Console.WriteLine(samples.Count == 0 ? NoData(dir) : StatsView.Hour(samples));
                return samples.Count == 0 ? 1 : 0;
            }

            var latest = store.ReadLatestSample();
            Console.WriteLine(latest is null ? NoData(dir) : StatsView.Latest(latest, DateTimeOffset.UtcNow));
            return latest is null ? 1 : 0;
        }

        public static int Events(string[] args, string dir)
        {
            var days = HealthThresholds.EventRetentionDays;
            var daysText = CliArgs.Option(args, "--days");
            if (daysText is not null
                && (!int.TryParse(daysText, NumberStyles.None, CultureInfo.InvariantCulture, out days) || days < 1))
            {
                Console.Error.WriteLine("fnctl: --days needs a positive number");
                return 2;
            }

            var events = new MetricsStore(dir, TimeProvider.System, createDirectory: false).ReadEvents(days);
            if (CliArgs.Flag(args, "--errors"))
            {
                events = events.Where(e => e.Kind is "error" or "error-summary").ToList();
            }
            Console.WriteLine(StatsView.Events(events));
            return 0;
        }

        public static async Task<int> WatchAsync(string dir)
        {
            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true; // Leave the loop and exit normally instead of being killed mid-draw.
                stop.Cancel();
            };

            var store = new MetricsStore(dir, TimeProvider.System, createDirectory: false);
            while (!stop.IsCancellationRequested)
            {
                var latest = store.ReadLatestSample();
                Console.Clear();
                Console.WriteLine(latest is null ? NoData(dir) : StatsView.Latest(latest, DateTimeOffset.UtcNow));
                Console.WriteLine();
                Console.WriteLine("Ctrl+C to stop");
                try
                {
                    await Task.Delay(WatchEvery, stop.Token);
                }
                catch (OperationCanceledException)
                {
                    break; // Ctrl+C: the normal way out.
                }
            }
            return 0;
        }

        private static string NoData(string dir) =>
            $"no samples in {dir} - the runner is not recording (is it running, and is the path right? try --dir)";
    }
}

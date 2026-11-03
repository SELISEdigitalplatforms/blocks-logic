namespace Blocks.FunctionRunner.Health
{
    /// <summary>
    /// The numbers the in-runner recorder and the root-side <c>fnctl police</c> judge the host by.
    /// <para>
    /// One home on purpose: the two run in different processes (the runner cannot clean up what
    /// root owns, so police exists), and if each kept its own copy of "too little memory" they
    /// would drift until one of them cried wolf while the other stayed quiet.
    /// </para>
    /// </summary>
    public static class HealthThresholds
    {
        /// <summary>Free memory below this share of the total is a breach.</summary>
        public const double MemAvailableMinPercent = 10;

        /// <summary>Swap used above this share of the swap size is a breach.</summary>
        public const double SwapUsedMaxPercent = 80;

        /// <summary>CPU busy above this share between two samples is a breach.</summary>
        public const double CpuMaxPercent = 90;

        /// <summary>The Docker data filesystem fuller than this is a breach.</summary>
        public const double DiskMaxPercent = 85;

        /// <summary>A stream whose consumer group is this many entries behind is a breach.</summary>
        public const long StreamLagMax = 100;

        /// <summary>How often the recorder writes a sample.</summary>
        public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(10);

        /// <summary>A newest sample older than this means the runner stopped recording.</summary>
        public static readonly TimeSpan SampleMaxAge = TimeSpan.FromSeconds(60);

        /// <summary>Sample files whose window ended longer ago than this are deleted.</summary>
        public static readonly TimeSpan SampleRetention = TimeSpan.FromMinutes(60);

        /// <summary>Length of the window one sample file covers.</summary>
        public static readonly TimeSpan SampleFileWindow = TimeSpan.FromMinutes(10);

        /// <summary>Event and police files older than this many days are deleted.</summary>
        public const int EventRetentionDays = 7;

        /// <summary>While a breach lasts, it is repeated at most this often.</summary>
        public static readonly TimeSpan EventReminder = TimeSpan.FromMinutes(10);

        /// <summary>At most this many runner errors become events per minute; the rest are counted.</summary>
        public const int ErrorEventsPerMinute = 60;

        /// <summary>
        /// A shim younger than this is never counted as orphaned by the recorder: Docker and the
        /// shim are not read in one instant, so a container being created or removed right now
        /// would look orphaned for a moment.
        /// </summary>
        public static readonly TimeSpan SampleOrphanGrace = TimeSpan.FromSeconds(30);

        /// <summary>Police only ends an orphaned shim that has been orphaned at least this long.</summary>
        public static readonly TimeSpan PoliceOrphanMinAge = TimeSpan.FromMinutes(2);

        /// <summary>How long a paused warm sandbox may outlive <c>WarmMaxAgeSeconds</c> before police fails it.</summary>
        public static readonly TimeSpan PausedExtraGrace = TimeSpan.FromMinutes(10);

        /// <summary>How long a running sandbox may outlive the hard limit before police fails it.</summary>
        public static readonly TimeSpan RunningExtraGrace = TimeSpan.FromMinutes(5);

        /// <summary>The image sweep runs hourly; no <c>gc-last.json</c> for this long means it stopped.</summary>
        public static readonly TimeSpan GcMaxAge = TimeSpan.FromHours(2);
    }
}

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Common.InternalService.Tracing
{
    /// <summary>
    /// Makes every span tag storable before Genesis's trace exporter sees it.
    /// <para>
    /// Root cause (found 2026-10-06): Genesis 4.2.4 <c>RedisClient</c> tags its
    /// <c>Redis::MessageReceived</c> span with <c>SetTag("MessageLength", redisValue.Length)</c> —
    /// <c>RedisValue.Length</c> is a method, so the tag value is a <c>Func&lt;long&gt;</c>, not a number.
    /// <c>MongoDBTraceExporter</c> converts every tag with <c>BsonValue.Create</c>, which throws on a
    /// delegate, and the whole batch is dropped ("Failed to insert trace batch for tenant
    /// 'miscellaneous'") — every span without a tenant (runner work, consumers, background), not
    /// just that one. The proper fix belongs in Genesis (call <c>Length()</c>, and convert tags
    /// one by one); this processor keeps traces flowing meanwhile and outlives that fix harmlessly.
    /// </para>
    /// <para>
    /// Registered before Genesis's own tracing setup, so it runs first in the processor chain. A
    /// parameterless numeric/bool/string getter (the Genesis case) is replaced by its value; any
    /// other value Mongo cannot store becomes its type name — never the object's text, which could
    /// hold data no one meant to trace.
    /// </para>
    /// </summary>
    public sealed class TraceTagSanitizer : BaseProcessor<Activity>
    {
        /// <summary>Adds the sanitizer to the tracer provider. Call before Genesis configures tracing.</summary>
        public static IServiceCollection AddFirst(IServiceCollection services) =>
            services.ConfigureOpenTelemetryTracerProvider(builder => builder.AddProcessor(new TraceTagSanitizer()));

        public override void OnEnd(Activity data)
        {
            List<KeyValuePair<string, object?>>? fixes = null;
            foreach (var tag in data.TagObjects)
            {
                if (IsStorable(tag.Value)) continue;
                (fixes ??= []).Add(new(tag.Key, Storable(tag.Value)));
            }
            if (fixes is null) return;
            foreach (var fix in fixes) data.SetTag(fix.Key, fix.Value);
        }

        internal static bool IsStorable(object? value) => value switch
        {
            null or string or bool or char => true,
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal => true,
            DateTime or DateTimeOffset or Guid or TimeSpan => true,
            string[] or bool[] or int[] or long[] or double[] => true,
            _ => false,
        };

        internal static object? Storable(object? value)
        {
            try
            {
                switch (value)
                {
                    case Func<long> f: return f();
                    case Func<int> f: return f();
                    case Func<double> f: return f();
                    case Func<bool> f: return f();
                    case Func<string> f: return f();
                }
            }
            catch (Exception)
            {
                return "unreadable";
            }
            return value?.GetType().Name;
        }
    }
}

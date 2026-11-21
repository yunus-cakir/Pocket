using System;
using System.Threading;
using Microsoft.Maui.Storage;

namespace Pocket.Client.Services
{
    /// <summary>
    /// Device clock corrected by the relay's clock (measured from /health), so a phone whose clock is off
    /// still stamps messages with the real time. The offset is kept across restarts for offline sends.
    /// </summary>
    public static class ServerClock
    {
        private const string OffsetKey = "ServerClockOffsetTicks";

        /// <summary>Offsets below this are network jitter, not a wrong clock.</summary>
        private static readonly TimeSpan IgnoreBelow = TimeSpan.FromSeconds(2);

        /// <summary>Slower round trips give too loose a measurement.</summary>
        private static readonly TimeSpan MaxRoundTrip = TimeSpan.FromSeconds(3);

        private static long _offsetTicks = LoadOffset();

        public static TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

        public static DateTime UtcNow => DateTime.UtcNow + Offset;

        /// <summary>Records the relay's time from a request sent at <paramref name="requestUtc"/> and answered at <paramref name="responseUtc"/> (device clock).</summary>
        public static void Observe(DateTime serverUtc, DateTime requestUtc, DateTime responseUtc)
        {
            var roundTrip = responseUtc - requestUtc;
            if (roundTrip < TimeSpan.Zero || roundTrip > MaxRoundTrip)
                return;

            var offset = AsUtc(serverUtc) - (requestUtc + roundTrip / 2);
            if (offset.Duration() < IgnoreBelow)
                offset = TimeSpan.Zero;

            if (Interlocked.Exchange(ref _offsetTicks, offset.Ticks) == offset.Ticks)
                return;

            System.Diagnostics.Debug.WriteLine($"[ServerClock] Device clock offset: {offset.TotalSeconds:F1}s");
            try { Preferences.Default.Set(OffsetKey, offset.Ticks); } catch { /* not persisted: re-measured on next connect */ }
        }

        /// <summary>
        /// Marks a time as UTC. sqlite-net reads stored ticks back as Unspecified and JSON without "Z" does the
        /// same; all our stored/sent times are UTC, so Unspecified means UTC (ToUniversalTime would treat it as local).
        /// </summary>
        public static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        private static long LoadOffset()
        {
            try { return Preferences.Default.Get(OffsetKey, 0L); }
            catch { return 0; }
        }
    }

    /// <summary>Which time an incoming payload is shown and sorted by.</summary>
    public static class PayloadTime
    {
        /// <summary>Reached the relay this long after it was sent → "arrived HH:mm" is shown too.</summary>
        public static readonly TimeSpan LateArrival = TimeSpan.FromMinutes(2);

        /// <summary>
        /// The sender's time, but never later than the relay received it: a sender whose clock is ahead
        /// (an older build without <see cref="ServerClock"/>) cannot date a message into the future.
        /// </summary>
        public static DateTime SentAt(Pocket.Shared.DTOs.EncryptedPayloadDto payload)
        {
            var sent = ServerClock.AsUtc(payload.Timestamp);
            if (payload.ServerReceivedAtUtc is DateTime received)
            {
                received = ServerClock.AsUtc(received);
                if (sent > received) sent = received;
            }
            return sent;
        }
    }
}

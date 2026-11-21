using System;
using Microsoft.Maui.Storage;

namespace Pocket.Client.Services
{
    /// <summary>
    /// Per-conversation "read up to" time (UTC ticks in Preferences), used to mark chat-list rows unread.
    /// Conversations that existed before unread tracking was added count as read up to its first start,
    /// so updating the app does not light up every old chat.
    /// </summary>
    public static class DmReadState
    {
        private const string KeyPrefix = "DmLastReadAt_";
        private const string TrackingStartKey = "DmReadTrackingStartTicks";

        private static readonly DateTime TrackingStart = LoadTrackingStart();

        public static DateTime GetLastReadAt(string friendId)
        {
            long ticks = 0;
            try { ticks = Preferences.Default.Get(KeyPrefix + friendId, 0L); } catch { }
            var stored = new DateTime(ticks, DateTimeKind.Utc);
            return stored > TrackingStart ? stored : TrackingStart;
        }

        /// <summary>Moves the read mark forward to <paramref name="readUpTo"/> (never backwards).</summary>
        public static void MarkRead(string friendId, DateTime readUpTo)
        {
            if (string.IsNullOrEmpty(friendId))
                return;

            var utc = ServerClock.AsUtc(readUpTo);
            if (utc <= GetLastReadAt(friendId))
                return;

            try { Preferences.Default.Set(KeyPrefix + friendId, utc.Ticks); } catch { }
        }

        private static DateTime LoadTrackingStart()
        {
            try
            {
                long ticks = Preferences.Default.Get(TrackingStartKey, 0L);
                if (ticks > 0)
                    return new DateTime(ticks, DateTimeKind.Utc);

                var now = ServerClock.UtcNow;
                Preferences.Default.Set(TrackingStartKey, now.Ticks);
                return now;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
    }
}

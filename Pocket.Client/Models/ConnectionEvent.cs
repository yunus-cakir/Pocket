using SQLite;
using System;

namespace Pocket.Client.Models
{
    public enum ConnectionEventKind
    {
        /// <summary>First connection of the session, or a connection to a newly configured server.</summary>
        Connected = 0,
        /// <summary>An established connection dropped.</summary>
        Disconnected = 1,
        /// <summary>Neither the primary nor the secondary server could be reached.</summary>
        Unreachable = 2,
        /// <summary>Connected to the secondary because the primary could not be reached.</summary>
        SwitchedToSecondary = 3,
        /// <summary>Moved from the secondary back to the primary.</summary>
        BackOnPrimary = 4,
        /// <summary>Connection came back on the same server after an outage.</summary>
        Reconnected = 5,
    }

    /// <summary>
    /// One entry of the relay connection history. Persisted so outage windows
    /// (Disconnected/Unreachable → next connect) can later be checked for lost data.
    /// </summary>
    public class ConnectionEvent
    {
        public const string PrimaryRole = "Primary";
        public const string SecondaryRole = "Secondary";

        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        public ConnectionEventKind Kind { get; set; }

        /// <summary>"Primary" or "Secondary"; empty when no server was involved.</summary>
        public string ServerRole { get; set; } = string.Empty;

        public string HubUrl { get; set; } = string.Empty;

        /// <summary>Error message or extra context.</summary>
        public string Detail { get; set; } = string.Empty;

        /// <summary>For (re)connect events after an outage: how long the app was offline.</summary>
        public double? DowntimeSeconds { get; set; }

        [Ignore]
        public string Title => Kind switch
        {
            ConnectionEventKind.Connected => "Connected",
            ConnectionEventKind.Disconnected => "Connection lost",
            ConnectionEventKind.Unreachable => "No server reachable",
            ConnectionEventKind.SwitchedToSecondary => "Switched to secondary",
            ConnectionEventKind.BackOnPrimary => "Back on primary",
            ConnectionEventKind.Reconnected => "Reconnected",
            _ => Kind.ToString()
        };

        [Ignore]
        public string ColorHex => Kind switch
        {
            ConnectionEventKind.Disconnected or ConnectionEventKind.Unreachable => "#EF4444",
            ConnectionEventKind.SwitchedToSecondary => "#F59E0B",
            _ => "#10B981"
        };

        [Ignore]
        public string Subtitle
        {
            get
            {
                string host = HubUrl;
                if (Uri.TryCreate(HubUrl, UriKind.Absolute, out var uri))
                    host = uri.Host;

                string text = string.IsNullOrEmpty(ServerRole) ? host
                    : string.IsNullOrEmpty(host) ? ServerRole
                    : $"{ServerRole} · {host}";

                if (DowntimeSeconds is double seconds && seconds >= 1)
                    text += $"  ·  offline {FormatDuration(TimeSpan.FromSeconds(seconds))}";

                return text;
            }
        }

        [Ignore]
        public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

        [Ignore]
        public string TimeText
        {
            get
            {
                var local = TimestampUtc.ToLocalTime();
                var today = DateTime.Now.Date;
                if (local.Date == today) return local.ToString("HH:mm:ss");
                if (local.Date == today.AddDays(-1)) return "Yesterday " + local.ToString("HH:mm");
                return local.ToString("dd MMM HH:mm");
            }
        }

        private static string FormatDuration(TimeSpan span)
        {
            if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
            if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m {span.Seconds}s";
            return $"{(int)span.TotalSeconds}s";
        }
    }
}

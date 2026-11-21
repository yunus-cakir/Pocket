using System.Collections.ObjectModel;
using System.Diagnostics;
using Pocket.Client.Data;
using Pocket.Client.Models;

namespace Pocket.Client.Services
{
    /// <summary>
    /// Persists relay connection events (drops, failovers, returns to the primary) and exposes
    /// the most recent ones for the Connection sheet. The stored outage windows are meant for
    /// later data-loss / history analysis via <see cref="GetEventsAsync"/>.
    /// </summary>
    public class ConnectionHistoryService
    {
        private const int VisibleLimit = 100;
        private static readonly TimeSpan Retention = TimeSpan.FromDays(90);

        private readonly LocalDatabase _database;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private bool _loaded;

        /// <summary>Most recent first. Only modified on the main thread.</summary>
        public ObservableCollection<ConnectionEvent> RecentEvents { get; } = new();

        public ConnectionHistoryService(LocalDatabase database, IRelayService relayService)
        {
            _database = database;
            relayService.OnConnectionEvent += evt => _ = SaveAsync(evt);
            _ = LoadAsync();
        }

        /// <summary>Events in the given UTC range, oldest first.</summary>
        public Task<List<ConnectionEvent>> GetEventsAsync(DateTime fromUtc, DateTime toUtc) =>
            _database.GetConnectionEventsAsync(fromUtc, toUtc);

        /// <summary>
        /// Whole stored history (up to the 90-day retention), oldest first, as a CSV file for
        /// outage / data-loss analysis. Times are written both in UTC and in the device's local time.
        /// </summary>
        public async Task<(string FileName, byte[] Content)> BuildExportCsvAsync()
        {
            var events = await _database.GetConnectionEventsAsync(DateTime.MinValue, DateTime.MaxValue);

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("timestamp_utc,timestamp_local,kind,title,server_role,hub_url,downtime_seconds,detail");
            foreach (var evt in events)
            {
                var utc = DateTime.SpecifyKind(evt.TimestampUtc, DateTimeKind.Utc);
                csv.Append(utc.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append(',')
                   .Append(utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")).Append(',')
                   .Append(evt.Kind).Append(',')
                   .Append(Escape(evt.Title)).Append(',')
                   .Append(Escape(evt.ServerRole)).Append(',')
                   .Append(Escape(evt.HubUrl)).Append(',')
                   .Append(evt.DowntimeSeconds is double s ? s.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) : string.Empty).Append(',')
                   .AppendLine(Escape(evt.Detail));
            }

            string fileName = $"pocket-connection-history-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
            // UTF-8 with BOM so Excel opens non-ASCII error messages correctly.
            var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            return (fileName, bytes);

            static string Escape(string? value)
            {
                value ??= string.Empty;
                return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
                    ? "\"" + value.Replace("\"", "\"\"") + "\""
                    : value;
            }
        }

        private async Task LoadAsync()
        {
            await _writeLock.WaitAsync();
            try
            {
                await _database.DeleteConnectionEventsBeforeAsync(DateTime.UtcNow - Retention);
                var events = await _database.GetRecentConnectionEventsAsync(VisibleLimit);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    // Events recorded before the load finished are already in the list (and in the DB).
                    var known = RecentEvents.Select(e => e.Id).ToHashSet();
                    foreach (var evt in events.Where(e => !known.Contains(e.Id)))
                        RecentEvents.Add(evt);
                    Trim();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConnectionHistory] Load failed: {ex.Message}");
            }
            finally
            {
                _loaded = true;
                _writeLock.Release();
            }
        }

        private async Task SaveAsync(ConnectionEvent evt)
        {
            await _writeLock.WaitAsync();
            try
            {
                await _database.SaveConnectionEventAsync(evt);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConnectionHistory] Save failed: {ex.Message}");
            }
            finally
            {
                _writeLock.Release();
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                RecentEvents.Insert(0, evt);
                if (_loaded) Trim();
            });
        }

        private void Trim()
        {
            while (RecentEvents.Count > VisibleLimit)
                RecentEvents.RemoveAt(RecentEvents.Count - 1);
        }
    }
}

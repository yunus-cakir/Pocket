using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Pocket.Client.Models;
using Pocket.Shared.DTOs;

namespace Pocket.Client.Services
{
    public class RelayService : IRelayService, IAsyncDisposable
    {
        private HubConnection? _hubConnection;

        /// <summary>Primary relay (Mac, PocketServer behind Tailscale Funnel).</summary>
        public const string DefaultServerUrl = "https://yunuscakir-mac.tail4d1102.ts.net";

        private const string ServerPreferenceKey = "ServerIp";
        // Bump the suffix whenever DefaultServerUrl changes, and add the previous default below.
        private const string ServerDefaultMigratedKey = "ServerIpDefaultMigrated_v4";
        private static readonly string[] LegacyDefaultServers =
        {
            "10.0.2.2",
            "localhost",
            "https://pc-1.tail4d1102.ts.net",
            "https://yunuscakir.tail4d1102.ts.net",
        };

        public static string GetDefaultServerIp() => DefaultServerUrl;

        /// <summary>
        /// Once per install (per migration version): a primary pointing at a previous built-in
        /// default moves to the current default, and when that happens a secondary that is now
        /// the same as the primary (the old built-in standby) becomes the new standby. After that
        /// explicitly chosen addresses are kept.
        /// </summary>
        private static void MigrateServerDefaults()
        {
            var prefs = Microsoft.Maui.Storage.Preferences.Default;
            if (prefs.Get(ServerDefaultMigratedKey, false))
                return;
            prefs.Set(ServerDefaultMigratedKey, true);

            string savedHub = BuildHubUrl(prefs.Get(ServerPreferenceKey, DefaultServerUrl));
            if (!LegacyDefaultServers.Any(l => string.Equals(BuildHubUrl(l), savedHub, StringComparison.OrdinalIgnoreCase)))
                return;

            prefs.Set(ServerPreferenceKey, DefaultServerUrl);
            if (prefs.ContainsKey(SecondaryServerPreferenceKey))
            {
                string secondary = prefs.Get(SecondaryServerPreferenceKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(secondary) &&
                    string.Equals(BuildHubUrl(secondary), BuildHubUrl(DefaultServerUrl), StringComparison.OrdinalIgnoreCase))
                    prefs.Set(SecondaryServerPreferenceKey, FallbackServerUrl);
            }
        }

        /// <summary>Saved server address, or the default.</summary>
        public static string GetSavedServer()
        {
            MigrateServerDefaults();
            string saved = Microsoft.Maui.Storage.Preferences.Default.Get(ServerPreferenceKey, DefaultServerUrl);
            return string.IsNullOrWhiteSpace(saved) ? DefaultServerUrl : saved;
        }

        public static string GetHubUrl() => BuildHubUrl(GetSavedServer());

        /// <summary>Standby relay (PC). See deploy/README.md.</summary>
        public const string FallbackServerUrl = "https://yunuscakir.tail4d1102.ts.net";

        private const string SecondaryServerPreferenceKey = "SecondaryServerIp";

        /// <summary>
        /// Saved secondary (standby) server, or the built-in PC standby when never set.
        /// An empty string means the user cleared it: no failover.
        /// </summary>
        public static string GetSavedSecondaryServer()
        {
            MigrateServerDefaults();
            return (Microsoft.Maui.Storage.Preferences.Default.Get(SecondaryServerPreferenceKey, FallbackServerUrl) ?? string.Empty).Trim();
        }

        /// <summary>Stores both relay addresses; an empty primary falls back to the default.</summary>
        public static void SaveServers(string? primary, string? secondary)
        {
            var prefs = Microsoft.Maui.Storage.Preferences.Default;
            string cleanPrimary = string.IsNullOrWhiteSpace(primary) ? DefaultServerUrl : primary.Trim();
            prefs.Set(ServerPreferenceKey, cleanPrimary);
            prefs.Set(SecondaryServerPreferenceKey, secondary?.Trim() ?? string.Empty);
        }

        /// <summary>Servers to try, in order: primary, then the secondary (if set and different).</summary>
        public static IReadOnlyList<string> GetServerCandidates()
        {
            string primary = GetSavedServer();
            string secondary = GetSavedSecondaryServer();
            bool hasSecondary = !string.IsNullOrWhiteSpace(secondary) &&
                                !string.Equals(BuildHubUrl(primary), BuildHubUrl(secondary), StringComparison.OrdinalIgnoreCase);
            return hasSecondary ? new[] { primary, secondary } : new[] { primary };
        }

        /// <summary>
        /// Accepts a bare host/IP ("192.168.1.35" → http://host:5200), host:port
        /// ("192.168.1.35:6000"), or a full URL ("https://pocket.example.com", e.g. a
        /// Cloudflare Tunnel hostname). "/hubs/relay" is appended when missing.
        /// </summary>
        public static string BuildHubUrl(string? server)
        {
            const string hubPath = "/hubs/relay";
            string value = string.IsNullOrWhiteSpace(server) ? GetDefaultServerIp() : server.Trim();

            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = value.TrimEnd('/');
                return value.EndsWith(hubPath, StringComparison.OrdinalIgnoreCase) ? value : value + hubPath;
            }

            value = value.TrimEnd('/');
            bool hasPort = value.LastIndexOf(':') > 0 && !value.Contains("::");
            return hasPort ? $"http://{value}{hubPath}" : $"http://{value}:5200{hubPath}";
        }


        // ───────────────────────── Device identity ─────────────────────────

        private const string DeviceIdFileName = "relay_device_id";
        private static string? _deviceId;

        /// <summary>
        /// Random id of this install. Several devices can share one identity (same user id and key, e.g. a
        /// restored database); relays use this id to deliver every message to each of them. Kept in the
        /// no-backup folder on Android so an auto-backup restore on another phone does not copy it.
        /// </summary>
        public static string GetDeviceId()
        {
            if (_deviceId != null) return _deviceId;
            try
            {
#if ANDROID
                string dir = Android.App.Application.Context.NoBackupFilesDir?.AbsolutePath
                             ?? Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
#else
                string dir = Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
#endif
                string path = Path.Combine(dir, DeviceIdFileName);
                if (File.Exists(path))
                {
                    string saved = File.ReadAllText(path).Trim();
                    if (saved.Length is > 0 and <= 64) return _deviceId = saved;
                }
                string id = Guid.NewGuid().ToString("N");
                File.WriteAllText(path, id);
                return _deviceId = id;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RelayService] Device id unavailable: {ex.Message}");
                return _deviceId = Guid.NewGuid().ToString("N"); // per process; still better than none
            }
        }

        // ───────────────────────── One connection per server ─────────────────────────

        /// <summary>
        /// A connection to one configured relay. The app keeps one per configured server and uses every
        /// one that is up: payloads are sent to all of them, so friends' devices get them whichever
        /// server they are on, and incoming copies are de-duplicated by message id.
        /// </summary>
        private sealed class RelayLink
        {
            public RelayLink(int index, string hubUrl)
            {
                Index = index;
                HubUrl = hubUrl;
            }

            public int Index { get; }
            public string HubUrl { get; }
            public HubConnection? Connection;
            public string? Instance;
            public CancellationTokenSource? RetryCts;
            public readonly SemaphoreSlim StartLock = new(1, 1);
            public bool IsConnected => Connection?.State == HubConnectionState.Connected;
        }

        private readonly object _linksLock = new();
        private List<RelayLink> _links = new();

        private List<RelayLink> Links
        {
            get { lock (_linksLock) return _links; }
        }

        private List<RelayLink> ConnectedLinks => Links.Where(l => l.IsConnected).ToList();

        public bool IsConnected => Links.Any(l => l.IsConnected);

        /// <summary>The first connected server in configured order (primary when it is up).</summary>
        public string CurrentHubUrl
        {
            get
            {
                var links = Links;
                return links.FirstOrDefault(l => l.IsConnected)?.HubUrl
                       ?? links.FirstOrDefault()?.HubUrl
                       ?? BuildHubUrl(GetSavedServer());
            }
        }

        public DateTime? ConnectedAt { get; private set; }

        public event Action<bool>? OnConnectionStateChanged;
        public event Action<EncryptedPayloadDto>? OnPayloadReceived;
        public event Action<Guid>? OnDeliveryAcknowledged;
        public event Action<Guid, string>? OnDeliveryAcknowledgedFrom;

        /// <summary>
        /// The running relay processes we are connected to, in configured order, joined with ';'
        /// (each "node|startedAt"). See <see cref="IsHeldBy"/>.
        /// </summary>
        public string? CurrentServerInstance
        {
            get
            {
                var instances = ConnectedLinks.Select(l => l.Instance).Where(i => !string.IsNullOrEmpty(i)).ToList();
                return instances.Count == 0 ? null : string.Join(";", instances);
            }
        }

        /// <summary>
        /// True when a payload accepted by <paramref name="storedInstances"/> is still held by a server we are
        /// connected to now (a relay process present in both lists). Otherwise it must be resent: relays keep
        /// payloads only in memory, so a restart or losing that server may have dropped it.
        /// </summary>
        public static bool IsHeldBy(string? storedInstances, string? currentInstances)
        {
            if (string.IsNullOrEmpty(storedInstances) || string.IsNullOrEmpty(currentInstances)) return false;
            var current = currentInstances.Split(';', StringSplitOptions.RemoveEmptyEntries);
            return storedInstances.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Any(s => current.Contains(s, StringComparer.Ordinal));
        }

        private record HealthResponse(string? Node, DateTime? StartedAt, DateTime? Now);

        /// <summary>Reads node + start time from /health (and the server's clock, if it sends one); null if unavailable.</summary>
        private static async Task<string?> FetchServerInstanceAsync(string hubUrl)
        {
            try
            {
                var requestUtc = DateTime.UtcNow;
                var health = await HealthClient.GetFromJsonAsync<HealthResponse>(GetBaseUrl(hubUrl) + "/health");
                if (health?.Now is DateTime serverNow)
                    ServerClock.Observe(serverNow, requestUtc, DateTime.UtcNow);
                if (health?.StartedAt == null) return null;
                return $"{health.Node}|{health.StartedAt.Value.ToUniversalTime():O}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RelayService] Server instance lookup failed: {ex.Message}");
                return null;
            }
        }
        public event Action<FriendRequestDto>? OnFriendRequestReceived;
        public event Action<FriendAcceptDto>? OnFriendAcceptReceived;

        /// <summary>Connection history: drops, failovers, returns to the primary (raised on any thread).</summary>
        public event Action<ConnectionEvent>? OnConnectionEvent;

        /// <summary>True while connected, but not to the first candidate (the primary is down).</summary>
        public bool IsOnFallback
        {
            get
            {
                var links = Links;
                return links.Count > 1 && !links[0].IsConnected && links.Skip(1).Any(l => l.IsConnected);
            }
        }

        // History tracking: the server we were last connected to, and when the current outage began.
        private readonly object _historyLock = new();
        private string? _lastRole;
        private string? _lastHubUrl;
        private DateTime? _outageStartedUtc;
        private bool _unreachableRecorded;
        private int _activeIndex = -1;

        private void RecordEvent(ConnectionEventKind kind, string role, string hubUrl, string? detail = null, double? downtimeSeconds = null)
        {
            var evt = new ConnectionEvent
            {
                TimestampUtc = DateTime.UtcNow,
                Kind = kind,
                ServerRole = role,
                HubUrl = hubUrl,
                Detail = detail ?? string.Empty,
                DowntimeSeconds = downtimeSeconds
            };
            System.Diagnostics.Debug.WriteLine($"[RelayService] History: {evt.Title} ({role} {hubUrl}) {detail}");
            try { OnConnectionEvent?.Invoke(evt); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RelayService] History handler failed: {ex.Message}"); }
        }

        /// <summary>An established connection dropped (or reconnecting began): starts an outage once.</summary>
        private void RecordConnectionLost(string hubUrl, Exception? error)
        {
            lock (_historyLock)
            {
                if (_outageStartedUtc != null) return;
                _outageStartedUtc = DateTime.UtcNow;
                RecordEvent(ConnectionEventKind.Disconnected, _lastRole ?? string.Empty, hubUrl, error?.Message);
            }
        }

        private void RecordConnected(int candidateIndex, string hubUrl)
        {
            lock (_historyLock)
            {
                string role = candidateIndex == 0 ? ConnectionEvent.PrimaryRole : ConnectionEvent.SecondaryRole;
                double? downtime = _outageStartedUtc is DateTime start ? (DateTime.UtcNow - start).TotalSeconds : null;
                bool sameServer = string.Equals(_lastHubUrl, hubUrl, StringComparison.OrdinalIgnoreCase);

                ConnectionEventKind? kind;
                string? detail = null;
                if (role == ConnectionEvent.SecondaryRole)
                {
                    if (_lastRole == ConnectionEvent.SecondaryRole && sameServer)
                        kind = downtime != null ? ConnectionEventKind.Reconnected : null;
                    else
                    {
                        kind = ConnectionEventKind.SwitchedToSecondary;
                        detail = "Primary server unreachable";
                    }
                }
                else if (_lastRole == ConnectionEvent.SecondaryRole)
                    kind = ConnectionEventKind.BackOnPrimary;
                else if (downtime != null)
                    kind = sameServer ? ConnectionEventKind.Reconnected : ConnectionEventKind.Connected;
                else if (_lastRole == null || !sameServer)
                    kind = ConnectionEventKind.Connected;
                else
                    kind = null; // routine reconnect to the same healthy server

                if (kind != null) RecordEvent(kind.Value, role, hubUrl, detail, downtime);

                _lastRole = role;
                _lastHubUrl = hubUrl;
                _outageStartedUtc = null;
                _unreachableRecorded = false;
            }
        }

        private void RecordUnreachable(string primaryHubUrl)
        {
            lock (_historyLock)
            {
                _outageStartedUtc ??= DateTime.UtcNow;
                if (_unreachableRecorded) return; // once per outage, not on every retry
                _unreachableRecorded = true;
                RecordEvent(ConnectionEventKind.Unreachable, string.Empty, primaryHubUrl,
                    GetServerCandidates().Count > 1 ? "Primary and secondary servers unreachable" : "Server unreachable");
            }
        }

        /// <summary>
        /// Re-evaluates the overall state after any link changed: history follows the "active" server (first
        /// connected in configured order), and listeners hear about every change so a newly reachable server
        /// triggers the outgoing flush.
        /// </summary>
        private void OnLinkStateChanged(RelayLink changed, Exception? error = null)
        {
            if (_stopped || _suppressLinkEvents) return;
            var links = Links;
            if (!links.Contains(changed)) return; // replaced by a newer ConnectAsync

            var active = links.FirstOrDefault(l => l.IsConnected);
            bool connected = active != null;
            lock (_historyLock)
            {
                int newIndex = active?.Index ?? -1;
                if (newIndex != _activeIndex)
                {
                    if (active != null)
                    {
                        if (_activeIndex < 0) ConnectedAt = DateTime.Now;
                        RecordConnected(active.Index, active.HubUrl);
                    }
                    else
                    {
                        ConnectedAt = null;
                        RecordConnectionLost(_lastHubUrl ?? changed.HubUrl, error);
                    }
                    _activeIndex = newIndex;
                }
                else if (active != null && ReferenceEquals(active, changed) && changed.IsConnected)
                {
                    // Same active server came back (automatic reconnect): logs downtime if there was an outage.
                    RecordConnected(active.Index, active.HubUrl);
                }
            }

            OnConnectionStateChanged?.Invoke(connected);
        }

        private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);
        private static readonly int[] RetryDelaysSeconds = { 2, 5, 10, 20, 30, 60 };

        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private string? _userId;
        private string? _username;
        private volatile bool _stopped;
        // Set while ConnectAsync tries all servers at once, so history records one outcome, not the race.
        private volatile bool _suppressLinkEvents;

        /// <summary>
        /// Connects to every configured server at once and returns when each has connected or failed its
        /// first attempt. Servers that are down keep retrying in the background.
        /// </summary>
        public async Task ConnectAsync(string userId, string username)
        {
            bool identityChanged = _userId != userId || _username != username;
            _userId = userId;
            _username = username;
            _stopped = false;

            await _connectLock.WaitAsync();
            try
            {
                var hubUrls = GetServerCandidates().Select(BuildHubUrl).ToList();
                var current = Links;
                bool sameServers = !identityChanged && current.Count == hubUrls.Count &&
                                   current.Select(l => l.HubUrl).SequenceEqual(hubUrls, StringComparer.OrdinalIgnoreCase);

                List<RelayLink> links;
                if (sameServers)
                {
                    links = current;
                }
                else
                {
                    links = hubUrls.Select((url, i) => new RelayLink(i, url)).ToList();
                    lock (_linksLock) _links = links;
                    lock (_historyLock) _activeIndex = -1;
                    foreach (var old in current) await StopLinkAsync(old);
                }

                // Try every server that is not up now (all of them on a fresh start) in parallel.
                _suppressLinkEvents = true;
                try
                {
                    await Task.WhenAll(links.Where(l => !l.IsConnected).Select(async link =>
                    {
                        if (!await TryStartLinkAsync(link)) StartRetryLoop(link);
                    }));
                }
                finally
                {
                    _suppressLinkEvents = false;
                }

                var first = links.FirstOrDefault(l => l.IsConnected);
                if (first != null)
                {
                    OnLinkStateChanged(first);
                }
                else
                {
                    ConnectedAt = null;
                    RecordUnreachable(links[0].HubUrl);
                    OnConnectionStateChanged?.Invoke(false);
                }
            }
            finally
            {
                _connectLock.Release();
            }
        }

        private static async Task StopLinkAsync(RelayLink link)
        {
            try { link.RetryCts?.Cancel(); } catch (ObjectDisposedException) { }
            link.RetryCts = null;
            var connection = link.Connection;
            link.Connection = null;
            link.Instance = null;
            if (connection != null)
            {
                try { await connection.DisposeAsync(); } catch { /* already closed */ }
            }
        }

        private async Task<bool> TryStartLinkAsync(RelayLink link)
        {
            await link.StartLock.WaitAsync();
            try
            {
                if (_stopped || !Links.Contains(link)) return false;
                if (link.IsConnected) return true;

                var old = link.Connection;
                link.Connection = null;
                if (old != null)
                {
                    try { await old.DisposeAsync(); } catch { /* already closed */ }
                }

                string hubUrl = link.HubUrl;
                string baseUrl = GetBaseUrl(hubUrl);
                // A fresh server-issued token per connection attempt (server restarts invalidate tokens).
                var connection = new HubConnectionBuilder()
                    .WithUrl(hubUrl, options => { options.AccessTokenProvider = () => FetchTokenAsync(baseUrl)!; })
                    .WithAutomaticReconnect()
                    .Build();

                // Lifecycle events are ignored once this connection has been replaced.
                connection.Closed += exception =>
                {
                    if (!ReferenceEquals(connection, link.Connection) || _stopped) return Task.CompletedTask;
                    link.Instance = null;
                    OnLinkStateChanged(link, exception);
                    StartRetryLoop(link); // automatic reconnect gave up: keep trying this server
                    return Task.CompletedTask;
                };

                connection.Reconnecting += exception =>
                {
                    if (ReferenceEquals(connection, link.Connection)) OnLinkStateChanged(link, exception);
                    return Task.CompletedTask;
                };

                connection.Reconnected += async _ =>
                {
                    if (!ReferenceEquals(connection, link.Connection)) return;
                    // Same URL, but the server may have restarted (and lost its queue) meanwhile.
                    link.Instance = await FetchServerInstanceAsync(hubUrl);
                    if (_lastRegisteredIdentity != null && link.IsConnected)
                    {
                        try { await connection.SendAsync("RegisterUser", _lastRegisteredIdentity); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RelayService] RegisterUser on {hubUrl} failed: {ex.Message}"); }
                    }
                    OnLinkStateChanged(link);
                };

                // Map Hub events to C# events. With several servers up the same payload can arrive more than once.
                connection.On<EncryptedPayloadDto>("ReceivePayload", payload =>
                {
                    if (BeginReceive(payload)) OnPayloadReceived?.Invoke(payload);
                });
                connection.On<Guid>("AcknowledgeDelivery", messageId => OnDeliveryAcknowledged?.Invoke(messageId));
                connection.On<Guid, string>("AcknowledgeDeliveryFrom",
                    (messageId, recipientId) => OnDeliveryAcknowledgedFrom?.Invoke(messageId, recipientId));
                connection.On<FriendRequestDto>("ReceiveFriendRequest", request =>
                {
                    if (IsFreshSignal($"req|{request.SenderId}")) OnFriendRequestReceived?.Invoke(request);
                });
                connection.On<FriendAcceptDto>("ReceiveFriendAccept", accept =>
                {
                    if (IsFreshSignal($"acc|{accept.SenderId}")) OnFriendAcceptReceived?.Invoke(accept);
                });

                try
                {
                    using var cts = new CancellationTokenSource(StartTimeout);
                    await connection.StartAsync(cts.Token);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RelayService] Failed to connect to {hubUrl}: {ex.Message}");
                    try { await connection.DisposeAsync(); } catch { }
                    return false;
                }

                if (_stopped || !Links.Contains(link))
                {
                    try { await connection.DisposeAsync(); } catch { }
                    return false;
                }

                link.Connection = connection;
                link.Instance = await FetchServerInstanceAsync(hubUrl);

                // Re-publish identity on this server (callers of ConnectAsync also register).
                if (_lastRegisteredIdentity != null)
                {
                    try { await connection.SendAsync("RegisterUser", _lastRegisteredIdentity); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RelayService] RegisterUser on {hubUrl} failed: {ex.Message}"); }
                }
            }
            finally
            {
                link.StartLock.Release();
            }

            OnLinkStateChanged(link);
            return true;
        }

        /// <summary>Retries one server with backoff until it is connected, replaced or the service stops.</summary>
        private void StartRetryLoop(RelayLink link)
        {
            if (_stopped || _userId == null || link.RetryCts != null) return;

            var cts = new CancellationTokenSource();
            link.RetryCts = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    for (int attempt = 0; !cts.IsCancellationRequested; attempt++)
                    {
                        int delay = RetryDelaysSeconds[Math.Min(attempt, RetryDelaysSeconds.Length - 1)];
                        await Task.Delay(TimeSpan.FromSeconds(delay), cts.Token);

                        if (link.IsConnected) return;
                        // Cheap check first: a standby that is switched off only costs a /health request.
                        if (!await IsHealthyAsync(link.HubUrl)) continue;
                        if (await TryStartLinkAsync(link)) return;
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    if (ReferenceEquals(link.RetryCts, cts)) link.RetryCts = null;
                    cts.Dispose();
                }
            });
        }

        // ───────────────────────── Duplicate suppression ─────────────────────────

        // Payloads handed to the app and not confirmed yet: a copy from another server arriving meanwhile is
        // dropped. Once confirmed (on every server) later copies go through again; the app ignores ids it
        // already stored and confirms them, which purges that server's copy for this device.
        private static readonly TimeSpan InFlightTimeout = TimeSpan.FromMinutes(2);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, Guid), DateTime> _inFlight = new();

        private bool BeginReceive(EncryptedPayloadDto payload)
        {
            var key = (payload.SenderId, payload.MessageId);
            var now = DateTime.UtcNow;
            if (_inFlight.TryGetValue(key, out var since) && now - since < InFlightTimeout) return false;
            _inFlight[key] = now;
            if (_inFlight.Count > 1000)
            {
                foreach (var (k, t) in _inFlight)
                    if (now - t >= InFlightTimeout) _inFlight.TryRemove(k, out _);
            }
            return true;
        }

        // Friend request/accept have no id: the same one from two servers within a few seconds is one event.
        private static readonly TimeSpan SignalDedupWindow = TimeSpan.FromSeconds(30);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _recentSignals = new();

        private bool IsFreshSignal(string key)
        {
            var now = DateTime.UtcNow;
            bool fresh = !_recentSignals.TryGetValue(key, out var at) || now - at >= SignalDedupWindow;
            if (fresh) _recentSignals[key] = now;
            return fresh;
        }

        private static string GetBaseUrl(string hubUrl)
        {
            const string hubPath = "/hubs/relay";
            return hubUrl.EndsWith(hubPath, StringComparison.OrdinalIgnoreCase)
                ? hubUrl[..^hubPath.Length]
                : hubUrl;
        }

        private Func<Task<(string PublicKey, string PrivateKey)>>? _identityKeyProvider;

        public void SetIdentityKeyProvider(Func<Task<(string PublicKey, string PrivateKey)>> provider)
        {
            _identityKeyProvider = provider;
        }

        private record ChallengeResponse(string Nonce);
        private record TokenResponse(string Token, DateTime ExpiresAt);

        /// <summary>
        /// Logs in by signing the server's one-time nonce with the identity key (the same P-256
        /// key used for E2EE); the server verifies it and returns a JWT it signed itself.
        /// </summary>
        private async Task<string> FetchTokenAsync(string baseUrl)
        {
            if (_identityKeyProvider == null || _userId == null)
                throw new InvalidOperationException("Identity key provider is not set.");

            var (publicKey, privateKey) = await _identityKeyProvider();
            if (string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(privateKey))
                throw new InvalidOperationException("Identity keys are not available.");

            var challenge = await AuthClient.GetFromJsonAsync<ChallengeResponse>(baseUrl + "/auth/challenge")
                            ?? throw new InvalidOperationException("Empty auth challenge.");

            byte[] signature;
            using (var ecdsa = System.Security.Cryptography.ECDsa.Create())
            {
                ecdsa.ImportECPrivateKey(Convert.FromBase64String(privateKey), out _);
                signature = ecdsa.SignData(
                    System.Text.Encoding.UTF8.GetBytes($"pocket-auth-v1|{_userId}|{challenge.Nonce}"),
                    System.Security.Cryptography.HashAlgorithmName.SHA256);
            }

            using var response = await AuthClient.PostAsJsonAsync(baseUrl + "/auth/token", new
            {
                userId = _userId,
                username = _username ?? string.Empty,
                publicKey,
                nonce = challenge.Nonce,
                signature = Convert.ToBase64String(signature),
                deviceId = GetDeviceId()
            });

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                throw new UnauthorizedAccessException("Server rejected this identity key for the user (key mismatch).");
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>()
                        ?? throw new InvalidOperationException("Empty auth token.");
            return token.Token;
        }

        private static readonly HttpClient AuthClient = new() { Timeout = TimeSpan.FromSeconds(10) };

        private static async Task<bool> IsHealthyAsync(string hubUrl)
        {
            string baseUrl = GetBaseUrl(hubUrl);
            try
            {
                using var response = await HealthClient.GetAsync(baseUrl + "/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            _stopped = true;
            foreach (var link in Links)
            {
                try { link.RetryCts?.Cancel(); } catch (ObjectDisposedException) { }
                link.RetryCts = null;
                var connection = link.Connection;
                if (connection != null)
                {
                    try { await connection.StopAsync(); } catch { /* already closed */ }
                }
            }
        }

        // Generous for a 15 MB photo/video over a slow uplink.
        private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(90);

        /// <summary>
        /// Sends to every connected server, so the recipient's devices get it whichever server they use.
        /// True when at least one server stored it.
        /// </summary>
        public async Task<bool> SendPayloadAsync(EncryptedPayloadDto payload)
        {
            var links = ConnectedLinks;
            if (links.Count == 0) return false;

            var results = await Task.WhenAll(links.Select(async link =>
            {
                var connection = link.Connection;
                if (connection == null || connection.State != HubConnectionState.Connected) return false;
                try
                {
                    // InvokeAsync completes only once the server ran SendPayload (payload stored in its memory).
                    // SendAsync would return as soon as the bytes were written locally — on a half-dead socket
                    // (server asleep, phone frozen) that "succeeds" and the payload is silently lost.
                    using var cts = new CancellationTokenSource(SendTimeout);
                    await connection.InvokeAsync("SendPayload", payload, cts.Token);
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RelayService] SendPayload {payload.MessageId} to {link.HubUrl} not acknowledged: {ex.Message}");
                    return false;
                }
            }));
            return results.Any(ok => ok);
        }

        /// <summary>Confirms on every connected server, so each drops its copy for this device.</summary>
        public async Task ConfirmDeliveryAsync(string senderId, Guid messageId)
        {
            _inFlight.TryRemove((senderId, messageId), out _);
            await SendToAllAsync("ConfirmDelivery", senderId, messageId);
        }

        private async Task SendToAllAsync(string method, params object?[] args)
        {
            await Task.WhenAll(ConnectedLinks.Select(async link =>
            {
                var connection = link.Connection;
                if (connection == null) return;
                try
                {
                    await connection.SendCoreAsync(method, args);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RelayService] {method} on {link.HubUrl} failed: {ex.Message}");
                }
            }));
        }

        private UserIdentityDto? _lastRegisteredIdentity;

        public async Task RegisterUserAsync(UserIdentityDto identity)
        {
            _lastRegisteredIdentity = identity;
            await SendToAllAsync("RegisterUser", identity);
        }

        /// <summary>Asks the connected servers in configured order; the first that knows the user answers.</summary>
        public async Task<UserIdentityDto?> LookupUserAsync(string username)
        {
            foreach (var link in ConnectedLinks)
            {
                var connection = link.Connection;
                if (connection == null) continue;
                try
                {
                    var identity = await connection.InvokeAsync<UserIdentityDto?>("LookupUser", username);
                    if (identity != null) return identity;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RelayService] LookupUser on {link.HubUrl} failed: {ex.Message}");
                }
            }

            return null;
        }

        public Task SendFriendRequestAsync(FriendRequestDto request) => SendToAllAsync("SendFriendRequest", request);

        public Task SendFriendAcceptAsync(FriendAcceptDto accept) => SendToAllAsync("SendFriendAccept", accept);

        public async ValueTask DisposeAsync()
        {
            _stopped = true;
            foreach (var link in Links)
            {
                await StopLinkAsync(link);
            }
        }
    }
}

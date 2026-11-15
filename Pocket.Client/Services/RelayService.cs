using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Pocket.Client.Models;
using Pocket.Shared.DTOs;

namespace Pocket.Client.Services
{
    public class RelayService : IRelayService, IAsyncDisposable
    {
        private HubConnection? _hubConnection;

        /// <summary>Public relay (PocketServer service behind Tailscale Funnel).</summary>
        public const string DefaultServerUrl = "https://yunuscakir.tail4d1102.ts.net";

        private const string ServerPreferenceKey = "ServerIp";
        // Bump the suffix whenever DefaultServerUrl changes, and add the previous default below.
        private const string ServerDefaultMigratedKey = "ServerIpDefaultMigrated_v3";
        private static readonly string[] LegacyDefaultServers =
        {
            "10.0.2.2",
            "localhost",
            "https://pc-1.tail4d1102.ts.net",
        };

        public static string GetDefaultServerIp() => DefaultServerUrl;

        /// <summary>
        /// Saved server address, or the default. Once per install (per migration version), a value
        /// pointing at a previous built-in default is moved to the current default; after that an
        /// explicitly chosen address is kept.
        /// </summary>
        public static string GetSavedServer()
        {
            var prefs = Microsoft.Maui.Storage.Preferences.Default;
            string saved = prefs.Get(ServerPreferenceKey, DefaultServerUrl);

            if (!prefs.Get(ServerDefaultMigratedKey, false))
            {
                prefs.Set(ServerDefaultMigratedKey, true);
                string savedHub = BuildHubUrl(saved);
                if (LegacyDefaultServers.Any(l => string.Equals(BuildHubUrl(l), savedHub, StringComparison.OrdinalIgnoreCase)))
                {
                    saved = DefaultServerUrl;
                    prefs.Set(ServerPreferenceKey, saved);
                }
            }

            return string.IsNullOrWhiteSpace(saved) ? DefaultServerUrl : saved;
        }

        public static string GetHubUrl() => BuildHubUrl(GetSavedServer());

        /// <summary>Standby relay (Mac). Only live while the primary is down — see deploy/README.md.</summary>
        public const string FallbackServerUrl = "https://yunuscakir-mac.tail4d1102.ts.net";

        private const string SecondaryServerPreferenceKey = "SecondaryServerIp";

        /// <summary>
        /// Saved secondary (standby) server, or the built-in Mac standby when never set.
        /// An empty string means the user cleared it: no failover.
        /// </summary>
        public static string GetSavedSecondaryServer() =>
            (Microsoft.Maui.Storage.Preferences.Default.Get(SecondaryServerPreferenceKey, FallbackServerUrl) ?? string.Empty).Trim();

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

        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
        public string CurrentHubUrl { get; private set; } = string.Empty;
        public DateTime? ConnectedAt { get; private set; }

        public event Action<bool>? OnConnectionStateChanged;
        public event Action<EncryptedPayloadDto>? OnPayloadReceived;
        public event Action<Guid>? OnDeliveryAcknowledged;
        public event Action<FriendRequestDto>? OnFriendRequestReceived;
        public event Action<FriendAcceptDto>? OnFriendAcceptReceived;

        /// <summary>Connection history: drops, failovers, returns to the primary (raised on any thread).</summary>
        public event Action<ConnectionEvent>? OnConnectionEvent;

        /// <summary>True while connected to a non-first candidate (the standby).</summary>
        public bool IsOnFallback { get; private set; }

        // History tracking: the server we were last connected to, and when the current outage began.
        private readonly object _historyLock = new();
        private string? _lastRole;
        private string? _lastHubUrl;
        private DateTime? _outageStartedUtc;
        private bool _unreachableRecorded;

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

        private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan PrimaryProbeInterval = TimeSpan.FromSeconds(30);
        private static readonly int[] FailoverDelaysSeconds = { 2, 5, 10, 20, 30, 60 };

        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private string? _userId;
        private string? _username;
        private bool _stopped;
        private CancellationTokenSource? _failoverCts;
        private Timer? _primaryProbeTimer;

        public async Task ConnectAsync(string userId, string username)
        {
            _userId = userId;
            _username = username;
            _stopped = false;
            CancelFailoverLoop();

            bool connected;
            await _connectLock.WaitAsync();
            try
            {
                connected = await ConnectToFirstAvailableAsync();
            }
            finally
            {
                _connectLock.Release();
            }

            if (!connected)
            {
                StartFailoverLoop();
            }
        }

        /// <summary>Tries each candidate in order; caller must hold _connectLock.</summary>
        private async Task<bool> ConnectToFirstAvailableAsync()
        {
            var candidates = GetServerCandidates();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (await TryStartAsync(BuildHubUrl(candidates[i])))
                {
                    IsOnFallback = i > 0;
                    RecordConnected(i, CurrentHubUrl);
                    UpdatePrimaryProbe();
                    OnConnectionStateChanged?.Invoke(true);
                    return true;
                }
            }

            // Show the primary as the target while nothing is reachable.
            CurrentHubUrl = BuildHubUrl(candidates[0]);
            IsOnFallback = false;
            UpdatePrimaryProbe();
            ConnectedAt = null;
            RecordUnreachable(CurrentHubUrl);
            OnConnectionStateChanged?.Invoke(false);
            return false;
        }

        private async Task<bool> TryStartAsync(string hubUrl)
        {
            var old = _hubConnection;
            _hubConnection = null;
            if (old != null)
            {
                try { await old.DisposeAsync(); } catch { /* already closed */ }
            }

            // A fresh server-issued token per connection attempt (server restarts invalidate tokens).
            string baseUrl = GetBaseUrl(hubUrl);
            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options => { options.AccessTokenProvider = () => FetchTokenAsync(baseUrl)!; })
                .WithAutomaticReconnect()
                .Build();

            // Lifecycle events are ignored once this connection has been replaced.
            connection.Closed += exception =>
            {
                if (!ReferenceEquals(connection, _hubConnection) || _stopped) return Task.CompletedTask;
                RecordConnectionLost(hubUrl, exception);
                OnConnectionStateChanged?.Invoke(false);
                StartFailoverLoop(); // automatic reconnect gave up: try the other server(s)
                return Task.CompletedTask;
            };

            connection.Reconnecting += exception =>
            {
                if (ReferenceEquals(connection, _hubConnection))
                {
                    RecordConnectionLost(hubUrl, exception);
                    OnConnectionStateChanged?.Invoke(false);
                }
                return Task.CompletedTask;
            };

            connection.Reconnected += async connectionId =>
            {
                if (!ReferenceEquals(connection, _hubConnection)) return;
                ConnectedAt = DateTime.Now;
                RecordConnected(IsOnFallback ? 1 : 0, hubUrl);
                if (_lastRegisteredIdentity != null && IsConnected)
                {
                    await RegisterUserAsync(_lastRegisteredIdentity);
                }

                OnConnectionStateChanged?.Invoke(true);
            };

            // Map Hub events to C# events
            connection.On<EncryptedPayloadDto>("ReceivePayload", payload => OnPayloadReceived?.Invoke(payload));
            connection.On<Guid>("AcknowledgeDelivery", messageId => OnDeliveryAcknowledged?.Invoke(messageId));
            connection.On<FriendRequestDto>("ReceiveFriendRequest",
                request => OnFriendRequestReceived?.Invoke(request));
            connection.On<FriendAcceptDto>("ReceiveFriendAccept", accept => OnFriendAcceptReceived?.Invoke(accept));

            try
            {
                using var cts = new CancellationTokenSource(StartTimeout);
                await connection.StartAsync(cts.Token);
                _hubConnection = connection;
                CurrentHubUrl = hubUrl;
                ConnectedAt = DateTime.Now;

                // Re-publish identity on the new server (callers of ConnectAsync also register).
                if (_lastRegisteredIdentity != null)
                {
                    await connection.SendAsync("RegisterUser", _lastRegisteredIdentity);
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RelayService] Failed to connect to {hubUrl}: {ex.Message}");
                try { await connection.DisposeAsync(); } catch { }
                return false;
            }
        }

        /// <summary>Retries all candidates with backoff until connected or stopped.</summary>
        private void StartFailoverLoop()
        {
            if (_stopped || _userId == null || _failoverCts != null) return;

            var cts = new CancellationTokenSource();
            _failoverCts = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    for (int attempt = 0; !cts.IsCancellationRequested; attempt++)
                    {
                        int delay = FailoverDelaysSeconds[Math.Min(attempt, FailoverDelaysSeconds.Length - 1)];
                        await Task.Delay(TimeSpan.FromSeconds(delay), cts.Token);

                        await _connectLock.WaitAsync(cts.Token);
                        try
                        {
                            if (IsConnected || await ConnectToFirstAvailableAsync()) return;
                        }
                        finally
                        {
                            _connectLock.Release();
                        }
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    if (ReferenceEquals(_failoverCts, cts)) _failoverCts = null;
                    cts.Dispose();
                }
            });
        }

        private void CancelFailoverLoop()
        {
            var cts = _failoverCts;
            _failoverCts = null;
            try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        }

        /// <summary>While on the standby, periodically check whether the primary is back.</summary>
        private void UpdatePrimaryProbe()
        {
            if (IsOnFallback)
            {
                _primaryProbeTimer ??= new Timer(_ => _ = ProbePrimaryAsync(), null, PrimaryProbeInterval, PrimaryProbeInterval);
            }
            else
            {
                _primaryProbeTimer?.Dispose();
                _primaryProbeTimer = null;
            }
        }

        private async Task ProbePrimaryAsync()
        {
            if (!IsOnFallback || _stopped) return;

            var primaryHub = BuildHubUrl(GetServerCandidates()[0]);
            if (!await IsHealthyAsync(primaryHub)) return;

            if (!await _connectLock.WaitAsync(0)) return;
            try
            {
                if (IsOnFallback && !_stopped)
                {
                    System.Diagnostics.Debug.WriteLine("[RelayService] Primary is back, switching from standby.");
                    if (!await ConnectToFirstAvailableAsync()) StartFailoverLoop();
                }
            }
            finally
            {
                _connectLock.Release();
            }
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
                signature = Convert.ToBase64String(signature)
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
            CancelFailoverLoop();
            _primaryProbeTimer?.Dispose();
            _primaryProbeTimer = null;
            if (_hubConnection != null)
            {
                await _hubConnection.StopAsync();
            }
        }

        public async Task SendPayloadAsync(EncryptedPayloadDto payload)
        {
            if (IsConnected)
            {
                await _hubConnection!.SendAsync("SendPayload", payload);
            }
        }

        public async Task ConfirmDeliveryAsync(string senderId, Guid messageId)
        {
            if (IsConnected)
            {
                await _hubConnection!.SendAsync("ConfirmDelivery", senderId, messageId);
            }
        }

        private UserIdentityDto? _lastRegisteredIdentity;

        public async Task RegisterUserAsync(UserIdentityDto identity)
        {
            _lastRegisteredIdentity = identity;
            if (IsConnected)
            {
                await _hubConnection!.SendAsync("RegisterUser", identity);
            }
        }

        public async Task<UserIdentityDto?> LookupUserAsync(string username)
        {
            if (IsConnected)
            {
                return await _hubConnection!.InvokeAsync<UserIdentityDto?>("LookupUser", username);
            }

            return null;
        }

        public async Task SendFriendRequestAsync(FriendRequestDto request)
        {
            if (IsConnected)
            {
                await _hubConnection!.SendAsync("SendFriendRequest", request);
            }
        }

        public async Task SendFriendAcceptAsync(FriendAcceptDto accept)
        {
            if (IsConnected)
            {
                await _hubConnection!.SendAsync("SendFriendAccept", accept);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stopped = true;
            CancelFailoverLoop();
            _primaryProbeTimer?.Dispose();
            if (_hubConnection != null)
            {
                await _hubConnection.DisposeAsync();
            }
        }
    }
}

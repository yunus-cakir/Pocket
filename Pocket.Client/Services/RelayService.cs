using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.IdentityModel.Tokens;
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

        /// <summary>
        /// Servers to try, in order. With the default server saved ("automatic" mode) this is
        /// primary → standby; a custom server is used on its own.
        /// </summary>
        public static IReadOnlyList<string> GetServerCandidates()
        {
            string saved = GetSavedServer();
            bool isDefault = string.Equals(BuildHubUrl(saved), BuildHubUrl(DefaultServerUrl), StringComparison.OrdinalIgnoreCase);
            return isDefault ? new[] { DefaultServerUrl, FallbackServerUrl } : new[] { saved };
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

        /// <summary>True while connected to a non-first candidate (the standby).</summary>
        public bool IsOnFallback { get; private set; }

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

            var token = GenerateDevJwt(_userId!, _username!);
            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options => { options.AccessTokenProvider = () => Task.FromResult(token)!; })
                .WithAutomaticReconnect()
                .Build();

            // Lifecycle events are ignored once this connection has been replaced.
            connection.Closed += exception =>
            {
                if (!ReferenceEquals(connection, _hubConnection) || _stopped) return Task.CompletedTask;
                OnConnectionStateChanged?.Invoke(false);
                StartFailoverLoop(); // automatic reconnect gave up: try the other server(s)
                return Task.CompletedTask;
            };

            connection.Reconnecting += exception =>
            {
                if (ReferenceEquals(connection, _hubConnection)) OnConnectionStateChanged?.Invoke(false);
                return Task.CompletedTask;
            };

            connection.Reconnected += async connectionId =>
            {
                if (!ReferenceEquals(connection, _hubConnection)) return;
                ConnectedAt = DateTime.Now;
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

        private static async Task<bool> IsHealthyAsync(string hubUrl)
        {
            const string hubPath = "/hubs/relay";
            string baseUrl = hubUrl.EndsWith(hubPath, StringComparison.OrdinalIgnoreCase)
                ? hubUrl[..^hubPath.Length]
                : hubUrl;
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

        /// <summary>
        /// Generates a dummy JWT for development purposes. 
        /// Since ValidateIssuerSigningKey = false on the server, any signature works.
        /// </summary>
        private string GenerateDevJwt(string userId, string username)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, username)
            };

            // Using an arbitrary key just to satisfy the token structure
            var key = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes("SuperSecretDummyKeyForDevelopmentPurposesOnly!!!"));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: "pocket-client-dev",
                audience: "pocket-server",
                claims: claims,
                expires: DateTime.Now.AddDays(7),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
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

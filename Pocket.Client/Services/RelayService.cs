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

        public async Task ConnectAsync(string userId, string username)
        {
            if (_hubConnection != null)
            {
                await _hubConnection.DisposeAsync();
            }

            var token = GenerateDevJwt(userId, username);
            CurrentHubUrl = GetHubUrl();

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(CurrentHubUrl, options => { options.AccessTokenProvider = () => Task.FromResult(token)!; })
                .WithAutomaticReconnect()
                .Build();

            // Connection Lifecycle Events
            _hubConnection.Closed += exception =>
            {
                OnConnectionStateChanged?.Invoke(false);
                return Task.CompletedTask;
            };

            _hubConnection.Reconnecting += exception =>
            {
                OnConnectionStateChanged?.Invoke(false);
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += async connectionId =>
            {
                ConnectedAt = DateTime.Now;
                if (_lastRegisteredIdentity != null && IsConnected)
                {
                    await RegisterUserAsync(_lastRegisteredIdentity);
                }

                OnConnectionStateChanged?.Invoke(true);
            };

            // Map Hub events to C# events
            _hubConnection.On<EncryptedPayloadDto>("ReceivePayload", payload => OnPayloadReceived?.Invoke(payload));
            _hubConnection.On<Guid>("AcknowledgeDelivery", messageId => OnDeliveryAcknowledged?.Invoke(messageId));
            _hubConnection.On<FriendRequestDto>("ReceiveFriendRequest",
                request => OnFriendRequestReceived?.Invoke(request));
            _hubConnection.On<FriendAcceptDto>("ReceiveFriendAccept", accept => OnFriendAcceptReceived?.Invoke(accept));

            try
            {
                await _hubConnection.StartAsync();
                ConnectedAt = DateTime.Now;
                OnConnectionStateChanged?.Invoke(true);
            }
            catch (Exception ex)
            {
                ConnectedAt = null;
                OnConnectionStateChanged?.Invoke(false);
                System.Diagnostics.Debug.WriteLine($"[RelayService] Failed to connect to SignalR: {ex.Message}");
            }
        }

        public async Task DisconnectAsync()
        {
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
            if (_hubConnection != null)
            {
                await _hubConnection.DisposeAsync();
            }
        }
    }
}

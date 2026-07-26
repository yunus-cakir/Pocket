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
        
        public static string GetDefaultServerIp()
        {
#if ANDROID
            return "10.0.2.2";
#else
            return "localhost";
#endif
        }

        public static string GetHubUrl()
        {
            string ip = Microsoft.Maui.Storage.Preferences.Get("ServerIp", GetDefaultServerIp());
            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = GetDefaultServerIp();
            }
            return $"http://{ip.Trim()}:5200/hubs/relay";
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
                .WithUrl(CurrentHubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(token)!;
                })
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
            _hubConnection.On<FriendRequestDto>("ReceiveFriendRequest", request => OnFriendRequestReceived?.Invoke(request));
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
            var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes("SuperSecretDummyKeyForDevelopmentPurposesOnly!!!"));
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

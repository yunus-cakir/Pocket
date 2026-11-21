using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Pocket.Shared.DTOs;
using Pocket.Shared.Interfaces;
using Pocket.Server.Services;
using System.Security.Claims;

namespace Pocket.Server.Hubs
{
    [Authorize]
    public class RelayHub : Hub<IRelayClient>
    {
        // Maps UserId to a set of ConnectionIds (supports multi-device)
        private static readonly ConcurrentDictionary<string, HashSet<string>> UserConnections = new();
        // ConnectionId -> DeviceId, to skip devices that already have a handed-off payload.
        private static readonly ConcurrentDictionary<string, string> ConnectionDevices = new();
        private static readonly ConcurrentDictionary<string, UserIdentityDto> ActiveUsernames = new(StringComparer.OrdinalIgnoreCase);
        private readonly TransientMemoryStore _memoryStore;
        private readonly ClusterState _cluster;
        private readonly IdentityRegistry _identities;

        public RelayHub(TransientMemoryStore memoryStore, ClusterState cluster, IdentityRegistry identities)
        {
            _memoryStore = memoryStore;
            _cluster = cluster;
            _identities = identities;
        }

        private string AuthenticatedUserId =>
            Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new HubException("Unauthorized.");

        /// <summary>The caller's device (one user may be signed in on several devices).</summary>
        private string DeviceId => DeviceIdOf(Context.User);

        private static string DeviceIdOf(ClaimsPrincipal? user)
        {
            var id = user?.FindFirst(AuthService.DeviceClaim)?.Value;
            return string.IsNullOrWhiteSpace(id) ? TransientMemoryStore.LegacyDeviceId : id;
        }

        /// <summary>The caller's registered identity key (bound at login via /auth/token).</summary>
        private string RegisteredPublicKey(string userId) =>
            _identities.GetPublicKey(userId) ?? throw new HubException("Unauthorized: unknown identity.");

        /// <summary>Current connection ids of a user (used to push handed-off payloads).</summary>
        public static string[] GetConnectionIds(string userId)
        {
            if (UserConnections.TryGetValue(userId, out var connections))
            {
                lock (connections)
                {
                    return connections.ToArray();
                }
            }
            return Array.Empty<string>();
        }

        /// <summary>Connection ids of a user's devices, except those in <paramref name="skipDevices"/>.</summary>
        public static string[] GetConnectionIds(string userId, IReadOnlySet<string> skipDevices) =>
            GetConnectionIds(userId)
                .Where(id => !ConnectionDevices.TryGetValue(id, out var device) || !skipDevices.Contains(device))
                .ToArray();

        public override async Task OnConnectedAsync()
        {
            if (_cluster.Draining)
            {
                // Handing off to the peer node: refuse so the client fails over there.
                Context.Abort();
                return;
            }
            _cluster.Track(Context);

            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var username = Context.User?.FindFirst(ClaimTypes.Name)?.Value ?? Context.User?.FindFirst("username")?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                var connections = UserConnections.GetOrAdd(userId, _ => new HashSet<string>());
                lock (connections)
                {
                    connections.Add(Context.ConnectionId);
                }
                ConnectionDevices[Context.ConnectionId] = DeviceId;
                _memoryStore.DeviceConnected(userId, DeviceId);

                // Deliver what this device has not confirmed yet (other devices may already have it).
                var pendingPayloads = _memoryStore.GetPendingPayloadsForDevice(userId, DeviceId);
                foreach (var payload in pendingPayloads)
                {
                    await Clients.Caller.ReceivePayload(payload);
                }

                // Delivery receipts that arrived while this user was offline.
                foreach (var (messageId, recipientId) in _memoryStore.TakeAcks(userId))
                {
                    await Clients.Caller.AcknowledgeDelivery(messageId);
                    await Clients.Caller.AcknowledgeDeliveryFrom(messageId, recipientId);
                }
            }

            await base.OnConnectedAsync();
        }

        /// <summary>Drops users' username lookups (after an identity sync changed their entry).</summary>
        public static void ForgetUsernames(IEnumerable<string> userIds)
        {
            var set = userIds.ToHashSet();
            foreach (var kvp in ActiveUsernames)
            {
                if (set.Contains(kvp.Value.UserId))
                {
                    ActiveUsernames.TryRemove(kvp.Key, out _);
                }
            }
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _cluster.Untrack(Context.ConnectionId);
            ConnectionDevices.TryRemove(Context.ConnectionId, out _);
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                _memoryStore.DeviceDisconnected(userId, DeviceId);
            }

            if (!string.IsNullOrEmpty(userId) && UserConnections.TryGetValue(userId, out var connections))
            {
                lock (connections)
                {
                    connections.Remove(Context.ConnectionId);
                    if (connections.Count == 0)
                    {
                        UserConnections.TryRemove(userId, out _);

                        // Remove this user's lookup entry once all devices disconnected (only their own).
                        foreach (var kvp in ActiveUsernames)
                        {
                            if (kvp.Value.UserId == userId)
                            {
                                ActiveUsernames.TryRemove(kvp.Key, out _);
                            }
                        }
                    }
                }
            }

            return base.OnDisconnectedAsync(exception);
        }

        // Publishes the caller for username lookup. UserId comes from the JWT and the key must be
        // the one proven at login, so nobody can advertise their own key under someone else's name.
        public Task RegisterUser(UserIdentityDto identity)
        {
            var userId = AuthenticatedUserId;

            if (userId != identity.UserId)
            {
                throw new HubException("Unauthorized: Token UserId does not match the provided Identity UserId.");
            }

            if (identity.PublicKey != RegisteredPublicKey(userId))
            {
                throw new HubException("Unauthorized: PublicKey does not match the registered identity.");
            }

            if (string.IsNullOrWhiteSpace(identity.Username) || !_identities.TryClaimUsername(userId, identity.Username))
            {
                throw new HubException("Username is already taken by another identity.");
            }

            // Remove any previous username mapping for this UserId if the user renamed their account
            foreach (var kvp in ActiveUsernames)
            {
                if (kvp.Value.UserId == identity.UserId && !string.Equals(kvp.Key, identity.Username, StringComparison.OrdinalIgnoreCase))
                {
                    ActiveUsernames.TryRemove(kvp.Key, out _);
                }
            }

            ActiveUsernames[identity.Username] = identity;
            return Task.CompletedTask;
        }

        public async Task SendPayload(EncryptedPayloadDto payload)
        {
            var authenticatedUserId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(authenticatedUserId))
            {
                payload.SenderId = authenticatedUserId;
            }

            // The relay's clock, not the sender's (a resend keeps the first stamp, see EnqueuePayload).
            payload.ServerReceivedAtUtc = DateTime.UtcNow;

            if (payload.MediaType == Pocket.Shared.Enums.MediaType.PostDeleted && payload.MediaPostId.HasValue)
            {
                _memoryStore.RemovePostFromUserQueue(payload.RecipientId, payload.SenderId, payload.MediaPostId.Value);
            }

            if (payload.MediaType == Pocket.Shared.Enums.MediaType.MessageDeleted && payload.MediaPostId.HasValue)
            {
                // Original never reached any device of the recipient: drop it, nothing to notify.
                bool undelivered = _memoryStore.IsUndelivered(payload.RecipientId, payload.MediaPostId.Value);
                if (_memoryStore.RemoveMessage(payload.RecipientId, payload.MediaPostId.Value) && undelivered)
                {
                    return;
                }
                // Otherwise some device has it: the removal keeps it off the others, the delete reaches the rest.
            }

            // Store transiently in memory until recipient explicitly confirms delivery (BR-201, BR-202)
            _memoryStore.EnqueuePayload(payload);

            if (UserConnections.TryGetValue(payload.RecipientId, out var connections) && connections.Count > 0)
            {
                string[] targetIds;
                lock (connections)
                {
                    targetIds = connections.ToArray();
                }

                foreach (var connectionId in targetIds)
                {
                    await Clients.Client(connectionId).ReceivePayload(payload);
                }
            }
        }

        public async Task ConfirmDelivery(string senderId, Guid messageId)
        {
            var recipientId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            if (!string.IsNullOrEmpty(recipientId))
            {
                // Purged once every active device of the recipient has confirmed it.
                _memoryStore.ConfirmDelivery(recipientId, DeviceId, messageId);
            }

            if (string.IsNullOrEmpty(recipientId)) return;

            string[] targetIds = GetConnectionIds(senderId);
            if (targetIds.Length == 0)
            {
                // Sender offline: keep the receipt (ids only) until it reconnects.
                _memoryStore.EnqueueAck(senderId, messageId, recipientId);
                return;
            }

            foreach (var senderConnectionId in targetIds)
            {
                await Clients.Client(senderConnectionId).AcknowledgeDelivery(messageId);
                await Clients.Client(senderConnectionId).AcknowledgeDeliveryFrom(messageId, recipientId);
            }
        }

        public Task<UserIdentityDto?> LookupUser(string username)
        {
            if (ActiveUsernames.TryGetValue(username, out var identity))
            {
                return Task.FromResult<UserIdentityDto?>(identity);
            }
            return Task.FromResult<UserIdentityDto?>(null);
        }

        public async Task SendFriendRequest(FriendRequestDto request)
        {
            // Sender identity and key come from the authenticated login, not from the client.
            var userId = AuthenticatedUserId;
            request.SenderId = userId;
            request.SenderPublicKey = RegisteredPublicKey(userId);

            if (UserConnections.TryGetValue(request.RecipientId, out var connections))
            {
                string[] targetIds;
                lock (connections)
                {
                    targetIds = connections.ToArray();
                }

                foreach (var connectionId in targetIds)
                {
                    await Clients.Client(connectionId).ReceiveFriendRequest(request);
                }
            }
        }

        public async Task SendFriendAccept(FriendAcceptDto accept)
        {
            var userId = AuthenticatedUserId;
            accept.SenderId = userId;
            accept.SenderPublicKey = RegisteredPublicKey(userId);

            if (UserConnections.TryGetValue(accept.RecipientId, out var connections))
            {
                string[] targetIds;
                lock (connections)
                {
                    targetIds = connections.ToArray();
                }

                foreach (var connectionId in targetIds)
                {
                    await Clients.Client(connectionId).ReceiveFriendAccept(accept);
                }
            }
        }
    }
}

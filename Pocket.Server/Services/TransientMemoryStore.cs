using System.Collections.Concurrent;
using Pocket.Shared.DTOs;

namespace Pocket.Server.Services
{
    /// <summary>
    /// Holds encrypted transient payloads in-memory ONLY until recipients pull/receive them.
    /// Strictly abides by BR-201 (Zero Persistent Cloud) & BR-202 (Delivery & Purge).
    ///
    /// Multi-device: one user (same UserId + identity key) may be signed in on several devices. A payload
    /// stays until every active device of the recipient has confirmed it, so a device that was offline
    /// when another one received the message still gets it. A device is active while connected and for
    /// <see cref="DeviceActiveWindow"/> after it was last seen, so a retired device does not pin payloads forever.
    /// </summary>
    public class TransientMemoryStore
    {
        /// <summary>Device id used for clients that do not send one (older builds): they share one slot.</summary>
        public const string LegacyDeviceId = "legacy";

        public static readonly TimeSpan DeviceActiveWindow = TimeSpan.FromDays(7);

        private sealed class PendingEntry
        {
            public required EncryptedPayloadDto Payload { get; set; }
            public HashSet<string> ConfirmedDevices { get; } = new(StringComparer.Ordinal);
        }

        private sealed class DeviceState
        {
            public int Connections;
            public DateTime LastSeenUtc;
        }

        // Outer key: UserId, Inner key: MessageId
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, PendingEntry>> _pendingPayloads = new();

        // Outer key: UserId, Inner key: DeviceId. Ids and timestamps only (BR-201).
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DeviceState>> _devices = new();

        /// <summary>A resend replaces the stored copy but keeps when the message first reached a relay.</summary>
        private static PendingEntry Replace(PendingEntry existing, EncryptedPayloadDto payload)
        {
            var first = existing.Payload.ServerReceivedAtUtc;
            if (first.HasValue && (payload.ServerReceivedAtUtc == null || first < payload.ServerReceivedAtUtc))
                payload.ServerReceivedAtUtc = first;
            existing.Payload = payload;
            return existing;
        }

        public void EnqueuePayload(EncryptedPayloadDto payload)
        {
            var userMessages = _pendingPayloads.GetOrAdd(payload.RecipientId, _ => new ConcurrentDictionary<Guid, PendingEntry>());
            // A resend of the same message id keeps the devices that already confirmed it.
            userMessages.AddOrUpdate(payload.MessageId,
                _ => new PendingEntry { Payload = payload },
                (_, existing) => Replace(existing, payload));
        }

        /// <summary>A device of the user connected (it is active while it has connections).</summary>
        public void DeviceConnected(string userId, string deviceId)
        {
            var state = _devices.GetOrAdd(userId, _ => new ConcurrentDictionary<string, DeviceState>())
                .GetOrAdd(deviceId, _ => new DeviceState());
            lock (state)
            {
                state.Connections++;
                state.LastSeenUtc = DateTime.UtcNow;
            }
        }

        public void DeviceDisconnected(string userId, string deviceId)
        {
            if (!_devices.TryGetValue(userId, out var devices) || !devices.TryGetValue(deviceId, out var state)) return;
            lock (state)
            {
                state.Connections = Math.Max(0, state.Connections - 1);
                state.LastSeenUtc = DateTime.UtcNow;
            }
        }

        private List<string> ActiveDevices(string userId, DateTime now)
        {
            if (!_devices.TryGetValue(userId, out var devices)) return new List<string>();
            var active = new List<string>();
            foreach (var (deviceId, state) in devices)
            {
                lock (state)
                {
                    if (state.Connections > 0 || now - state.LastSeenUtc < DeviceActiveWindow) active.Add(deviceId);
                }
            }
            return active;
        }

        /// <summary>
        /// Payloads this device has not confirmed yet (read without removing, so a dropped connection
        /// loses nothing). Messages are only removed once confirmed.
        /// </summary>
        public List<EncryptedPayloadDto> GetPendingPayloadsForDevice(string userId, string deviceId)
        {
            if (!_pendingPayloads.TryGetValue(userId, out var userMessages))
                return new List<EncryptedPayloadDto>();

            var result = new List<EncryptedPayloadDto>();
            foreach (var entry in userMessages.Values)
            {
                lock (entry)
                {
                    if (!entry.ConfirmedDevices.Contains(deviceId)) result.Add(entry.Payload);
                }
            }
            return result;
        }

        /// <summary>One pending payload as handed to the peer: who already has it and which devices the recipient has.</summary>
        public sealed record HandoffItem(EncryptedPayloadDto Payload, string[] ConfirmedDevices,
            Dictionary<string, DateTime> RecipientDevices);

        /// <summary>
        /// Snapshot of every pending payload for the standby → primary handoff, with the devices that
        /// already confirmed it and the recipient's active devices, so the peer neither sends it again
        /// to a device that has it nor forgets a device that is still waiting for it.
        /// </summary>
        public List<HandoffItem> SnapshotForHandoff()
        {
            var now = DateTime.UtcNow;
            var result = new List<HandoffItem>();
            foreach (var (userId, userMessages) in _pendingPayloads)
            {
                var devices = new Dictionary<string, DateTime>(StringComparer.Ordinal);
                if (_devices.TryGetValue(userId, out var known))
                {
                    foreach (var (deviceId, state) in known)
                    {
                        lock (state)
                        {
                            // A connected device counts as seen now (the handoff disconnects it anyway).
                            var seen = state.Connections > 0 ? now : state.LastSeenUtc;
                            if (now - seen < DeviceActiveWindow) devices[deviceId] = seen;
                        }
                    }
                }

                foreach (var entry in userMessages.Values)
                {
                    string[] confirmed;
                    lock (entry) confirmed = entry.ConfirmedDevices.ToArray();
                    result.Add(new HandoffItem(entry.Payload, confirmed, devices));
                }
            }
            return result;
        }

        /// <summary>
        /// Stores a payload handed over by the peer. Its confirmations are kept (merged with any copy we
        /// already hold) and the recipient's devices it knew are remembered, so the payload stays until
        /// those devices confirmed it too. Returns every device that already has it.
        /// </summary>
        public IReadOnlySet<string> AcceptHandoff(EncryptedPayloadDto payload, IEnumerable<string> confirmedDevices,
            IReadOnlyDictionary<string, DateTime> recipientDevices)
        {
            var now = DateTime.UtcNow;
            var devices = _devices.GetOrAdd(payload.RecipientId, _ => new ConcurrentDictionary<string, DeviceState>());
            foreach (var (deviceId, seen) in recipientDevices)
            {
                var lastSeen = seen > now ? now : seen;
                if (now - lastSeen >= DeviceActiveWindow) continue;
                var state = devices.GetOrAdd(deviceId, _ => new DeviceState { LastSeenUtc = lastSeen });
                lock (state)
                {
                    if (lastSeen > state.LastSeenUtc) state.LastSeenUtc = lastSeen;
                }
            }

            var userMessages = _pendingPayloads.GetOrAdd(payload.RecipientId, _ => new ConcurrentDictionary<Guid, PendingEntry>());
            var entry = userMessages.AddOrUpdate(payload.MessageId,
                _ => new PendingEntry { Payload = payload },
                (_, existing) => Replace(existing, payload));
            lock (entry)
            {
                foreach (var deviceId in confirmedDevices) entry.ConfirmedDevices.Add(deviceId);
                return entry.ConfirmedDevices.ToHashSet(StringComparer.Ordinal);
            }
        }

        /// <summary>
        /// Records that one device of the recipient has the message. The payload is purged once every
        /// active device of the recipient has confirmed it.
        /// </summary>
        public void ConfirmDelivery(string userId, string deviceId, Guid messageId)
        {
            if (!_pendingPayloads.TryGetValue(userId, out var userMessages) ||
                !userMessages.TryGetValue(messageId, out var entry))
                return;

            var active = ActiveDevices(userId, DateTime.UtcNow);
            lock (entry)
            {
                entry.ConfirmedDevices.Add(deviceId);
                if (active.All(entry.ConfirmedDevices.Contains))
                {
                    userMessages.TryRemove(messageId, out _);
                }
            }
        }

        /// <summary>Removes a message regardless of which devices have it (e.g. deleted before delivery).</summary>
        public bool RemoveMessage(string userId, Guid messageId)
        {
            if (_pendingPayloads.TryGetValue(userId, out var userMessages))
            {
                return userMessages.TryRemove(messageId, out _);
            }
            return false;
        }

        /// <summary>True when no device of the recipient has confirmed the message yet.</summary>
        public bool IsUndelivered(string userId, Guid messageId)
        {
            if (!_pendingPayloads.TryGetValue(userId, out var userMessages) ||
                !userMessages.TryGetValue(messageId, out var entry))
                return false;
            lock (entry)
            {
                return entry.ConfirmedDevices.Count == 0;
            }
        }

        /// <summary>
        /// Drops devices not seen within <see cref="DeviceActiveWindow"/> and purges payloads every
        /// remaining active device has confirmed (the retired device was the only one holding them).
        /// </summary>
        public void Sweep()
        {
            var now = DateTime.UtcNow;
            foreach (var (userId, devices) in _devices)
            {
                foreach (var (deviceId, state) in devices)
                {
                    bool stale;
                    lock (state)
                    {
                        stale = state.Connections == 0 && now - state.LastSeenUtc >= DeviceActiveWindow;
                    }
                    if (stale) devices.TryRemove(deviceId, out _);
                }
            }

            foreach (var (userId, userMessages) in _pendingPayloads)
            {
                var active = ActiveDevices(userId, now);
                foreach (var (messageId, entry) in userMessages)
                {
                    lock (entry)
                    {
                        // Never-confirmed payloads wait for the recipient as before.
                        if (entry.ConfirmedDevices.Count > 0 && active.All(entry.ConfirmedDevices.Contains))
                            userMessages.TryRemove(messageId, out _);
                    }
                }
            }
        }

        // Delivery receipts for senders that were offline when the recipient confirmed.
        // Outer key: sender UserId, inner key: (MessageId, recipient UserId). Ids only, no content (BR-201).
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<(Guid MessageId, string RecipientId), byte>> _pendingAcks = new();

        public void EnqueueAck(string senderId, Guid messageId, string recipientId)
        {
            var acks = _pendingAcks.GetOrAdd(senderId, _ => new ConcurrentDictionary<(Guid, string), byte>());
            acks[(messageId, recipientId)] = 0;
        }

        /// <summary>Removes and returns the receipts waiting for a sender.</summary>
        public List<(Guid MessageId, string RecipientId)> TakeAcks(string senderId)
        {
            return _pendingAcks.TryRemove(senderId, out var acks)
                ? acks.Keys.ToList()
                : new List<(Guid, string)>();
        }

        public void RemovePostFromUserQueue(string userId, string senderId, Guid postId)
        {
            if (_pendingPayloads.TryGetValue(userId, out var userMessages))
            {
                userMessages.TryRemove(postId, out _);
                foreach (var kvp in userMessages.ToArray())
                {
                    var payload = kvp.Value.Payload;
                    if (payload.SenderId == senderId && payload.MediaPostId == postId &&
                        payload.MediaType != Pocket.Shared.Enums.MediaType.PostDeleted)
                    {
                        userMessages.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }
    }

    /// <summary>Runs <see cref="TransientMemoryStore.Sweep"/> hourly.</summary>
    public class TransientStoreSweeper : BackgroundService
    {
        private readonly TransientMemoryStore _store;

        public TransientStoreSweeper(TransientMemoryStore store) => _store = store;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _store.Sweep();
            }
        }
    }
}

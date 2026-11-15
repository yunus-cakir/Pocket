using System.Text.Json;

namespace Pocket.Server.Services
{
    /// <summary>
    /// Persistent binding of UserId → identity public key (and claimed username).
    /// Trust on first use: the first key that proves possession for a UserId owns it from then on,
    /// so a forged login for an existing user is rejected, and a username can't be re-registered
    /// with someone else's key (which would let an attacker intercept friend requests).
    /// Only public identity data is stored — no messages or media (BR-201).
    /// </summary>
    public class IdentityRegistry
    {
        /// <param name="FirstSeenUtc">When this key first logged in for the UserId (TOFU order across nodes).</param>
        /// <param name="UsernameSetUtc">When the current username was claimed (null for entries written before sync existed).</param>
        public record Entry(string PublicKey, string? Username, DateTime FirstSeenUtc, DateTime? UsernameSetUtc = null);

        /// <summary>What a merge changed locally. Users in KeyReplaced/UsernameChanged should be reconnected.</summary>
        public record MergeResult(int Added, IReadOnlyList<string> KeyReplaced, IReadOnlyList<string> UsernameChanged, int Updated)
        {
            public bool Changed => Added > 0 || Updated > 0;
        }

        private readonly string _path;
        private readonly object _lock = new();
        private readonly Dictionary<string, Entry> _byUserId;
        private readonly ILogger<IdentityRegistry> _logger;

        public IdentityRegistry(IConfiguration configuration, IHostEnvironment environment, ILogger<IdentityRegistry> logger)
        {
            _logger = logger;
            _path = configuration["Auth:RegistryPath"]
                    ?? Path.Combine(environment.ContentRootPath, "data", "identities.json");

            _byUserId = new Dictionary<string, Entry>();
            if (File.Exists(_path))
            {
                try
                {
                    _byUserId = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path),
                                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) // peer sync writes camelCase
                                ?? new Dictionary<string, Entry>();
                    if (_byUserId.Values.Any(e => e is null || string.IsNullOrWhiteSpace(e.PublicKey)))
                        throw new InvalidDataException("an entry has no public key");
                }
                catch (Exception ex)
                {
                    // Refuse to start with an unreadable registry rather than silently re-opening TOFU.
                    throw new InvalidOperationException($"Identity registry '{_path}' is unreadable: {ex.Message}", ex);
                }
            }
            _logger.LogInformation("Identity registry: {Count} identities ({Path})", _byUserId.Count, _path);
        }

        public enum BindResult { Existing, New, KeyMismatch }

        /// <summary>Binds the key to the user on first use; afterwards only that key is accepted.</summary>
        public BindResult Bind(string userId, string publicKey)
        {
            lock (_lock)
            {
                if (_byUserId.TryGetValue(userId, out var entry))
                {
                    return entry.PublicKey == publicKey ? BindResult.Existing : BindResult.KeyMismatch;
                }

                _byUserId[userId] = new Entry(publicKey, null, DateTime.UtcNow);
                Save();
                return BindResult.New;
            }
        }

        public string? GetPublicKey(string userId)
        {
            lock (_lock)
            {
                return _byUserId.TryGetValue(userId, out var entry) ? entry.PublicKey : null;
            }
        }

        /// <summary>Claims (or renames to) a username; fails if another user already holds it.</summary>
        public bool TryClaimUsername(string userId, string username)
        {
            lock (_lock)
            {
                if (!_byUserId.TryGetValue(userId, out var entry)) return false;

                foreach (var (otherId, other) in _byUserId)
                {
                    if (otherId != userId && string.Equals(other.Username, username, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                if (!string.Equals(entry.Username, username, StringComparison.Ordinal))
                {
                    _byUserId[userId] = entry with { Username = username, UsernameSetUtc = DateTime.UtcNow };
                    Save();
                }
                return true;
            }
        }

        public int Count { get { lock (_lock) return _byUserId.Count; } }

        public Dictionary<string, Entry> Snapshot()
        {
            lock (_lock) return new Dictionary<string, Entry>(_byUserId);
        }

        /// <summary>
        /// Merges a peer node's registry into this one (PC ⇄ Mac). Deterministic, so both nodes
        /// converge to the same result whichever side merges first:
        /// - same UserId, different keys: the key seen first keeps the UserId (TOFU across nodes);
        /// - same UserId and key: the most recently claimed username wins;
        /// - two UserIds with the same username: the earlier claim keeps it, the other loses it
        ///   and has to pick a new name (their next RegisterUser is refused).
        /// </summary>
        public MergeResult Merge(IReadOnlyDictionary<string, Entry>? incoming)
        {
            lock (_lock)
            {
                var merged = new Dictionary<string, Entry>(_byUserId);
                foreach (var (userId, theirs) in incoming ?? new Dictionary<string, Entry>())
                {
                    if (string.IsNullOrWhiteSpace(userId) || theirs is null || string.IsNullOrWhiteSpace(theirs.PublicKey))
                        continue;
                    merged[userId] = merged.TryGetValue(userId, out var ours) ? Pick(ours, theirs) : theirs;
                }
                ResolveUsernameClashes(merged);

                int added = 0, updated = 0;
                var keyReplaced = new List<string>();
                var usernameChanged = new List<string>();
                foreach (var (userId, entry) in merged)
                {
                    if (!_byUserId.TryGetValue(userId, out var old)) { added++; continue; }
                    if (old == entry) continue;
                    updated++;
                    if (old.PublicKey != entry.PublicKey)
                    {
                        keyReplaced.Add(userId);
                        _logger.LogWarning("Identity sync: {UserId} is bound to a different key on the peer since earlier; the peer's key wins.", userId);
                    }
                    else if (!string.Equals(old.Username, entry.Username, StringComparison.Ordinal))
                    {
                        usernameChanged.Add(userId);
                    }
                }

                if (added > 0 || updated > 0)
                {
                    _byUserId.Clear();
                    foreach (var (userId, entry) in merged) _byUserId[userId] = entry;
                    Save();
                }
                return new MergeResult(added, keyReplaced, usernameChanged, updated);
            }
        }

        private static Entry Pick(Entry a, Entry b)
        {
            if (a.PublicKey != b.PublicKey)
            {
                int byAge = a.FirstSeenUtc.CompareTo(b.FirstSeenUtc);
                if (byAge == 0) byAge = string.CompareOrdinal(a.PublicKey, b.PublicKey);
                return byAge <= 0 ? a : b;
            }

            int byName = (a.UsernameSetUtc ?? DateTime.MinValue).CompareTo(b.UsernameSetUtc ?? DateTime.MinValue);
            if (byName == 0) byName = string.CompareOrdinal(a.Username ?? "", b.Username ?? "");
            var named = byName >= 0 ? a : b;
            var firstSeen = a.FirstSeenUtc <= b.FirstSeenUtc ? a.FirstSeenUtc : b.FirstSeenUtc;
            return new Entry(a.PublicKey, named.Username, firstSeen, named.UsernameSetUtc);
        }

        private void ResolveUsernameClashes(Dictionary<string, Entry> merged)
        {
            var clashes = merged
                .Where(kv => !string.IsNullOrEmpty(kv.Value.Username))
                .GroupBy(kv => kv.Value.Username!, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in clashes)
            {
                var ordered = group
                    .OrderBy(kv => kv.Value.UsernameSetUtc ?? kv.Value.FirstSeenUtc)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .ToList();
                foreach (var (userId, entry) in ordered.Skip(1))
                {
                    _logger.LogWarning("Identity sync: username '{Username}' was claimed by two users; {UserId} loses it.", entry.Username, userId);
                    // Stamped with now, so this release outranks the stale claim still on the other node.
                    merged[userId] = entry with { Username = null, UsernameSetUtc = DateTime.UtcNow };
                }
            }
        }

        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_byUserId, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _path, overwrite: true);
        }
    }
}

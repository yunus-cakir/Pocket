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
        public record Entry(string PublicKey, string? Username, DateTime FirstSeenUtc);

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
                    _byUserId = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path))
                                ?? new Dictionary<string, Entry>();
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
                    _byUserId[userId] = entry with { Username = username };
                    Save();
                }
                return true;
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

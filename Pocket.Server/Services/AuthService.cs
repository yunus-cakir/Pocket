using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Pocket.Server.Services
{
    /// <summary>
    /// Proof-of-possession login: the client signs a one-time server nonce with its identity
    /// key (the same P-256 key its friends use for E2EE), and gets a JWT signed with a random
    /// per-process key that never leaves the server. Clients fetch a fresh token per connection,
    /// so tokens simply stop working after a server restart.
    /// </summary>
    public class AuthService
    {
        public const string Issuer = "pocket-relay";
        public const string Audience = "pocket-client";
        private static readonly TimeSpan NonceLifetime = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(12);
        private const int MaxOutstandingNonces = 10_000;

        /// <summary>Random per-process JWT signing key (shared with the JwtBearer validation setup).</summary>
        public static SymmetricSecurityKey CreateSigningKey() => new(RandomNumberGenerator.GetBytes(32));

        private readonly SymmetricSecurityKey _signingKey;
        private readonly ConcurrentDictionary<string, DateTime> _nonces = new();
        private readonly IdentityRegistry _registry;
        private readonly ILogger<AuthService> _logger;

        public AuthService(SymmetricSecurityKey signingKey, IdentityRegistry registry, ILogger<AuthService> logger)
        {
            _signingKey = signingKey;
            _registry = registry;
            _logger = logger;
        }

        public static byte[] ChallengeBytes(string userId, string nonce) =>
            Encoding.UTF8.GetBytes($"pocket-auth-v1|{userId}|{nonce}");

        public string IssueNonce()
        {
            var now = DateTime.UtcNow;
            if (_nonces.Count > MaxOutstandingNonces)
            {
                foreach (var (key, expiry) in _nonces)
                    if (expiry < now) _nonces.TryRemove(key, out _);
            }

            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _nonces[nonce] = now + NonceLifetime;
            return nonce;
        }

        public enum Outcome { Ok, BadRequest, Unauthorized, KeyMismatch }

        public (Outcome Outcome, string? Token, DateTime ExpiresAt) IssueToken(
            string userId, string username, string publicKey, string nonce, string signature)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(publicKey) ||
                string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(signature))
                return (Outcome.BadRequest, null, default);

            // One-time, short-lived nonce (prevents replaying a captured signature).
            if (!_nonces.TryRemove(nonce, out var expiry) || expiry < DateTime.UtcNow)
                return (Outcome.Unauthorized, null, default);

            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
                if (!ecdsa.VerifyData(ChallengeBytes(userId, nonce), Convert.FromBase64String(signature), HashAlgorithmName.SHA256))
                    return (Outcome.Unauthorized, null, default);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                return (Outcome.BadRequest, null, default);
            }

            var bind = _registry.Bind(userId, publicKey);
            if (bind == IdentityRegistry.BindResult.KeyMismatch)
            {
                _logger.LogWarning("Rejected login for {UserId}: key does not match the registered identity.", userId);
                return (Outcome.KeyMismatch, null, default);
            }
            if (bind == IdentityRegistry.BindResult.New)
            {
                _logger.LogInformation("Registered new identity {UserId}.", userId);
            }

            var expiresAt = DateTime.UtcNow + TokenLifetime;
            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: Audience,
                claims: new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Name, username ?? string.Empty)
                },
                expires: expiresAt,
                signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

            return (Outcome.Ok, new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
        }
    }
}

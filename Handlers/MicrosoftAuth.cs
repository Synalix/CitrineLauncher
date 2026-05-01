using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace CitrineLauncher.Handlers
{
    public static class MicrosoftAuth
    {
        private static readonly JELoginHandler _handler = JELoginHandlerBuilder.BuildDefault();

        // Keyed by stable Account.Id so each Microsoft account has its own session cache.
        private record CacheEntry(MSession Session, DateTime Expiry);
        private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private static readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Opens the system browser for Microsoft OAuth. Returns the Minecraft username and session.
        /// Pass the stable account id so the result is stored under the correct cache key.
        /// </summary>
        public static async Task<(string Username, MSession Session)> AuthenticateAsync(string accountId)
        {
            var session = await _handler.AuthenticateInteractively();
            SetCache(accountId, session);
            return (session.Username, session);
        }

        /// <summary>
        /// Returns a cached session for the given account id if still valid, otherwise attempts a
        /// silent token refresh. Falls back to interactive login if tokens are missing or expired.
        /// </summary>
        public static async Task<MSession> GetSessionAsync(string accountId)
        {
            if (_cache.TryGetValue(accountId, out var entry) && DateTime.UtcNow < entry.Expiry)
                return entry.Session;

            try
            {
                var session = await _handler.AuthenticateSilently();
                SetCache(accountId, session);
                return session;
            }
            catch (Exception)
            {
                // CmlLib does not expose a specific cache-miss exception type;
                // fall back to interactive for any auth failure.
                var (_, session) = await AuthenticateAsync(accountId);
                return session;
            }
        }

        /// <summary>Invalidates the cached session for a specific account (call on sign-out or re-auth).</summary>
        public static void ClearCache(string accountId)
        {
            _cache.TryRemove(accountId, out _);
        }

        /// <summary>Invalidates all cached sessions.</summary>
        public static void ClearAllCaches()
        {
            _cache.Clear();
        }

        /// <summary>
        /// Re-keys a cached session from one account id to another.
        /// Used when re-authing an existing account to avoid a second interactive login.
        /// </summary>
        public static void ReKeyCache(string fromAccountId, string toAccountId)
        {
            if (_cache.TryRemove(fromAccountId, out var entry))
                _cache[toAccountId] = entry;
        }

        /// <summary>
        /// Returns the cached username for validation, or null if not cached.
        /// </summary>
        public static string? GetCachedUsername(string accountId)
        {
            return _cache.TryGetValue(accountId, out var entry)
                ? entry.Session.Username
                : null;
        }

        private static void SetCache(string accountId, MSession session)
        {
            _cache[accountId] = new CacheEntry(session, DateTime.UtcNow.Add(_cacheTtl));
        }
    }
}

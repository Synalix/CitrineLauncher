using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using System;
using System.Threading.Tasks;

namespace CitrineLauncher.Handlers
{
    public static class MicrosoftAuth
    {
        private static readonly JELoginHandler _handler = JELoginHandlerBuilder.BuildDefault();

        private static MSession? _cachedSession;
        private static DateTime _cacheExpiry = DateTime.MinValue;
        private static readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Opens the system browser for Microsoft OAuth. Returns the Minecraft username and session.
        /// Clears the session cache so the next GetSessionAsync fetches fresh tokens.
        /// </summary>
        public static async Task<(string Username, MSession Session)> AuthenticateAsync()
        {
            var session = await _handler.AuthenticateInteractively();
            SetCache(session);
            return (session.Username, session);
        }

        /// <summary>
        /// Returns a cached session if still valid, otherwise attempts a silent token refresh.
        /// Falls back to interactive login if tokens are missing or expired.
        /// </summary>
        public static async Task<MSession> GetSessionAsync()
        {
            if (_cachedSession != null && DateTime.UtcNow < _cacheExpiry)
                return _cachedSession;

            try
            {
                var session = await _handler.AuthenticateSilently();
                SetCache(session);
                return session;
            }
            catch (Exception)
            {
                // CmlLib does not expose a specific cache-miss exception type;
                // fall back to interactive for any auth failure.
                var (_, session) = await AuthenticateAsync();
                return session;
            }
        }

        /// <summary>Invalidates the cached session (call on sign-out).</summary>
        public static void ClearCache()
        {
            _cachedSession = null;
            _cacheExpiry = DateTime.MinValue;
        }

        private static void SetCache(MSession session)
        {
            _cachedSession = session;
            _cacheExpiry = DateTime.UtcNow.Add(_cacheTtl);
        }
    }
}

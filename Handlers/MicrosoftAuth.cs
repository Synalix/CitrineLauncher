using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using System;
using System.Threading.Tasks;

namespace CitrineLauncher.Handlers
{
    public static class MicrosoftAuth
    {
        private static readonly JELoginHandler _handler = JELoginHandlerBuilder.BuildDefault();

        /// <summary>
        /// Opens the system browser for Microsoft OAuth. Returns the Minecraft username and session.
        /// </summary>
        public static async Task<(string Username, MSession Session)> AuthenticateAsync()
        {
            var session = await _handler.AuthenticateInteractively();
            return (session.Username, session);
        }

        /// <summary>
        /// Attempts a silent token refresh from CmlLib cache.
        /// Falls back to interactive login if tokens are missing or expired.
        /// </summary>
        public static async Task<MSession> GetSessionAsync()
        {
            try
            {
                return await _handler.AuthenticateSilently();
            }
            catch (Exception)
            {
                // CmlLib does not expose a specific cache-miss exception type;
                // fall back to interactive for any auth failure.
                var (_, session) = await AuthenticateAsync();
                return session;
            }
        }
    }
}

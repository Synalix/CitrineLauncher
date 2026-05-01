using CmlLib.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CitrineLauncher.Handlers
{
    public static class GameVersionCache
    {
        private static List<string>? _releaseVersions;
        private static readonly SemaphoreSlim _fetchLock = new(1, 1);

        public static bool NeedsRefresh()
        {
            var cached = Settings.Instance.CachedGameVersions;
            var cachedAt = Settings.Instance.GameVersionsCachedAt;

            if (cached == null || cached.Count == 0)
                return true;

            var dayAgo = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (24 * 60 * 60);
            return cachedAt < dayAgo;
        }

        public static async Task<List<string>> GetReleaseVersionsAsync()
        {
            if (_releaseVersions != null && _releaseVersions.Count > 0)
                return _releaseVersions;

            await _fetchLock.WaitAsync();
            try
            {
                // Re-check after acquiring the lock — another caller may have fetched while we waited.
                if (_releaseVersions != null && _releaseVersions.Count > 0)
                    return _releaseVersions;

                var cached = Settings.Instance.CachedGameVersions;
                if (cached != null && cached.Count > 0)
                {
                    _releaseVersions = cached;
                    return _releaseVersions;
                }

                // Fetch from Mojang
                var launcher = new MinecraftLauncher(new MinecraftPath(Settings.Instance.MinecraftPath));
                var versions = await launcher.GetAllVersionsAsync();

                _releaseVersions = versions
                    .Where(v => v.Type == "release" && !string.IsNullOrEmpty(v.Name))
                    .Select(v => v.Name!)
                    .OrderByDescending(v => v)
                    .ToList();

                Settings.Instance.CachedGameVersions = _releaseVersions;
                Settings.Instance.GameVersionsCachedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                return _releaseVersions;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GameVersionCache: fetch failed {ex.Message}");
                // Fallback to in-memory cache, then settings cache
                if (_releaseVersions != null && _releaseVersions.Count > 0)
                    return _releaseVersions;
                var cached = Settings.Instance.CachedGameVersions;
                return cached ?? new List<string>();
            }
            finally
            {
                _fetchLock.Release();
            }
        }

        public static List<string> GetCached()
        {
            return Settings.Instance.CachedGameVersions ?? new List<string>();
        }
    }
}
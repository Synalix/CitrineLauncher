using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CitrineLauncher.Handlers
{
    /// <summary>
    /// Thin wrapper around the Modrinth v2 API for searching and downloading modpacks.
    /// </summary>
    public static class ModrinthClient
    {
        private static readonly HttpClient _http = new HttpClient
        {
            DefaultRequestHeaders = { { "User-Agent", "CitrineLauncher/1.0" } }
        };

        private const string BaseUrl = "https://api.modrinth.com/v2";

        public record ModrinthProject(
            string ProjectId,
            string Slug,
            string Title,
            string Description,
            string? IconUrl,
            string[] GameVersions,
            int Downloads
        );

        public record ModrinthVersion(
            string VersionId,
            string VersionNumber,
            string GameVersion,
            string DownloadUrl,
            string FileName
        );

        /// <summary>
        /// Search for modpacks by query. Optionally filter to a specific game version.
        /// </summary>
        public static async Task<List<ModrinthProject>> SearchAsync(
            string query,
            string? gameVersion = null,
            int limit = 20,
            CancellationToken ct = default)
        {
            // Build facets: always filter to modpack project type
            string facets;
            if (!string.IsNullOrEmpty(gameVersion))
                facets = $"[[\"project_type:modpack\"],[\"versions:{gameVersion}\"]]";
            else
                facets = "[[\"project_type:modpack\"]]";

            var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(facets)}&limit={limit}&index=relevance";
            var json = await _http.GetStringAsync(url, ct);

            using var doc = JsonDocument.Parse(json);
            var hits = doc.RootElement.GetProperty("hits");
            var results = new List<ModrinthProject>();

            foreach (var hit in hits.EnumerateArray())
            {
                var id = hit.GetProperty("project_id").GetString() ?? string.Empty;
                var slug = hit.GetProperty("slug").GetString() ?? string.Empty;
                var title = hit.GetProperty("title").GetString() ?? string.Empty;
                var desc = hit.GetProperty("description").GetString() ?? string.Empty;
                var icon = hit.TryGetProperty("icon_url", out var iconEl) ? iconEl.GetString() : null;
                var downloads = hit.GetProperty("downloads").GetInt32();

                var gameVersions = new List<string>();
                if (hit.TryGetProperty("versions", out var versionsEl))
                    foreach (var v in versionsEl.EnumerateArray())
                        gameVersions.Add(v.GetString() ?? string.Empty);

                results.Add(new ModrinthProject(id, slug, title, desc, icon, gameVersions.ToArray(), downloads));
            }

            return results;
        }

        /// <summary>
        /// Get the latest modpack version for a project, optionally filtered to a game version.
        /// Returns null if no compatible version is found.
        /// </summary>
        public static async Task<ModrinthVersion?> GetLatestVersionAsync(
            string projectId,
            string? gameVersion = null,
            CancellationToken ct = default)
        {
            var url = $"{BaseUrl}/project/{projectId}/version";
            if (!string.IsNullOrEmpty(gameVersion))
                url += $"?game_versions=[\"{gameVersion}\"]";

            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;

            // First element is the latest
            foreach (var ver in arr.EnumerateArray())
            {
                var versionId = ver.GetProperty("id").GetString() ?? string.Empty;
                var versionNumber = ver.GetProperty("version_number").GetString() ?? string.Empty;

                // Pick the first .mrpack file
                if (!ver.TryGetProperty("files", out var filesEl)) continue;
                foreach (var file in filesEl.EnumerateArray())
                {
                    var filename = file.GetProperty("filename").GetString() ?? string.Empty;
                    if (!filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)) continue;

                    var downloadUrl = file.GetProperty("url").GetString() ?? string.Empty;

                    // Resolve actual game version from the version's game_versions array
                    string resolvedGameVersion = gameVersion ?? string.Empty;
                    if (ver.TryGetProperty("game_versions", out var gvEl))
                        foreach (var gv in gvEl.EnumerateArray())
                        {
                            resolvedGameVersion = gv.GetString() ?? resolvedGameVersion;
                            break;
                        }

                    return new ModrinthVersion(versionId, versionNumber, resolvedGameVersion, downloadUrl, filename);
                }
            }

            return null;
        }

        /// <summary>
        /// Download a modpack file to a temp path, reporting progress (0–100).
        /// Returns the local file path.
        /// </summary>
        public static async Task<string> DownloadToTempAsync(
            ModrinthVersion version,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"citrine_modrinth_{Guid.NewGuid():N}.mrpack");

            using var response = await _http.GetAsync(version.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(tempPath);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                if (total > 0)
                    progress?.Report((int)(downloaded * 100 / total));
            }

            return tempPath;
        }
    }
}

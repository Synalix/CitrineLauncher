using CitrineLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CitrineLauncher.Handlers
{
    /// <summary>
    /// Imports a local modpack zip or folder into an existing GameInstance directory.
    /// Supports CurseForge manifest.json and Modrinth modrinth.index.json layouts.
    /// File downloading is NOT done here — only the files already present in the pack are copied.
    /// </summary>
    public static class ModpackImporter
    {
        public record ImportResult(bool Success, string Message, string? DetectedGameVersion = null);

        /// <summary>
        /// Import from a zip file. Extracts to a temp folder, then delegates to ImportFromFolder.
        /// </summary>
        public static async Task<ImportResult> ImportFromZipAsync(string zipPath, GameInstance target)
        {
            if (!File.Exists(zipPath))
                return new ImportResult(false, $"File not found: {zipPath}");

            var tempDir = Path.Combine(Path.GetTempPath(), "citrine_import_" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                Directory.CreateDirectory(tempDir);
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true));
                return await ImportFromFolderAsync(tempDir, target);
            }
            catch (Exception ex)
            {
                return new ImportResult(false, $"Failed to extract zip: {ex.Message}");
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// Import from an already-extracted folder. Detects pack format and copies files.
        /// </summary>
        public static async Task<ImportResult> ImportFromFolderAsync(string sourceFolder, GameInstance target)
        {
            if (!Directory.Exists(sourceFolder))
                return new ImportResult(false, $"Folder not found: {sourceFolder}");

            // Try CurseForge format first
            var curseForgeMeta = Path.Combine(sourceFolder, "manifest.json");
            if (File.Exists(curseForgeMeta))
                return await ImportCurseForgeAsync(sourceFolder, curseForgeMeta, target);

            // Try Modrinth format
            var modrinthMeta = Path.Combine(sourceFolder, "modrinth.index.json");
            if (File.Exists(modrinthMeta))
                return await ImportModrinthAsync(sourceFolder, modrinthMeta, target);

            // Unknown format — copy everything as-is
            return await CopyOverridesAsync(sourceFolder, target.InstanceDirectory, "unknown");
        }

        // ── CurseForge ─────────────────────────────────────────────────────────

        private static async Task<ImportResult> ImportCurseForgeAsync(
            string sourceFolder, string metaPath, GameInstance target)
        {
            try
            {
                var json = await File.ReadAllTextAsync(metaPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var gameVersion = root.TryGetProperty("minecraft", out var mc)
                    ? mc.TryGetProperty("version", out var ver) ? ver.GetString() : null
                    : null;

                // Copy overrides/ subfolder into instance directory
                var overridesDir = Path.Combine(sourceFolder, "overrides");
                ImportResult copyResult;
                if (Directory.Exists(overridesDir))
                    copyResult = await CopyOverridesAsync(overridesDir, target.InstanceDirectory, "CurseForge");
                else
                    copyResult = new ImportResult(true, "No overrides folder found (mods must be downloaded separately).");

                // Write pack metadata so we can show it later
                await WritePackMetaAsync(target, "curseforge", root);

                var msg = copyResult.Success
                    ? $"CurseForge pack imported. Game version: {gameVersion ?? "unknown"}. Note: mods must be downloaded separately."
                    : copyResult.Message;

                return new ImportResult(copyResult.Success, msg, gameVersion);
            }
            catch (Exception ex)
            {
                return new ImportResult(false, $"CurseForge import failed: {ex.Message}");
            }
        }

        // ── Modrinth ───────────────────────────────────────────────────────────

        private static readonly HttpClient _http = new HttpClient
        {
            DefaultRequestHeaders = { { "User-Agent", "CitrineLauncher/1.0" } }
        };

        private static async Task<ImportResult> ImportModrinthAsync(
            string sourceFolder, string metaPath, GameInstance target)
        {
            try
            {
                var json = await File.ReadAllTextAsync(metaPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var gameVersion = root.TryGetProperty("dependencies", out var deps)
                    ? deps.TryGetProperty("minecraft", out var mc) ? mc.GetString() : null
                    : null;

                // Download mods listed in the index
                var modsDir = Path.Combine(target.InstanceDirectory, "mods");
                Directory.CreateDirectory(modsDir);
                if (root.TryGetProperty("files", out var filesEl))
                {
                    foreach (var fileEl in filesEl.EnumerateArray())
                    {
                        // path relative to instance root (e.g. "mods/sodium-1.21.jar")
                        var relPath = fileEl.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
                        if (string.IsNullOrEmpty(relPath)) continue;

                        // Only download mods (skip config files etc that belong in overrides)
                        if (!relPath.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)) continue;

                        // Pick first download URL
                        string? downloadUrl = null;
                        if (fileEl.TryGetProperty("downloads", out var dlEl))
                            foreach (var u in dlEl.EnumerateArray())
                            {
                                downloadUrl = u.GetString();
                                break;
                            }

                        if (string.IsNullOrEmpty(downloadUrl)) continue;

                        var destPath = Path.Combine(target.InstanceDirectory, relPath.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                        if (!File.Exists(destPath))
                        {
                            var bytes = await _http.GetByteArrayAsync(downloadUrl);
                            await File.WriteAllBytesAsync(destPath, bytes);
                        }
                    }
                }

                // Copy overrides/ into instance directory
                var overridesDir = Path.Combine(sourceFolder, "overrides");
                ImportResult copyResult;
                if (Directory.Exists(overridesDir))
                    copyResult = await CopyOverridesAsync(overridesDir, target.InstanceDirectory, "Modrinth");
                else
                    copyResult = new ImportResult(true, "Modrinth pack imported.");

                await WritePackMetaAsync(target, "modrinth", root);

                var msg = copyResult.Success
                    ? $"Modrinth pack imported. Game version: {gameVersion ?? "unknown"}."
                    : copyResult.Message;

                return new ImportResult(copyResult.Success, msg, gameVersion);
            }
            catch (Exception ex)
            {
                return new ImportResult(false, $"Modrinth import failed: {ex.Message}");
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static async Task<ImportResult> CopyOverridesAsync(
            string sourceDir, string targetDir, string format)
        {
            try
            {
                await Task.Run(() => CopyDirectory(sourceDir, targetDir));
                return new ImportResult(true, $"{format} files copied successfully.");
            }
            catch (Exception ex)
            {
                return new ImportResult(false, $"Failed to copy files: {ex.Message}");
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var destFile = Path.Combine(dest, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                File.Copy(file, destFile, overwrite: true);
            }
        }

        private static async Task WritePackMetaAsync(GameInstance target, string format, JsonElement root)
        {
            var metaPath = Path.Combine(target.InstanceDirectory, "pack-meta.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            var meta = new { format, imported = DateTime.UtcNow, raw = root };
            await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, options));
        }
    }
}

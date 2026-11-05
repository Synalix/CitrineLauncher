using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CitrineLauncher.Handlers;

public record ModrinthProject(string ProjectId, string Slug, string Title, string Description, string? IconUrl, string[] GameVersions, long Downloads);
public record ModrinthDependency(string? ProjectId, string? VersionId, string Type);
public record ModrinthVersion(string VersionId, string ProjectId, string VersionNumber, string[] GameVersions,
    string[] Loaders, string DownloadUrl, string FileName, string? Sha512, string? Sha1, ModrinthDependency[] Dependencies)
{
    public override string ToString() => VersionNumber;
}
public record ModrinthSearchPage(List<ModrinthProject> Projects, int Total, int Offset);

public static class ModrinthClient
{
    public static HttpClient Http { get; } = CreateHttp();
    public static ModrinthApi Api { get; } = new(Http);
    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CitrineLauncher/1.2.3 (+https://github.com/Synalix/CitrineLauncher)");
        return http;
    }
}

/// <summary>Modrinth v2 API; injectable HTTP transport for offline contract tests.</summary>
public sealed class ModrinthApi(HttpClient http)
{
    private const string BaseUrl = "https://api.modrinth.com/v2";
    private static string EncodeArray(string value) => Uri.EscapeDataString(JsonSerializer.Serialize(new[] { value }));

    public async Task<ModrinthSearchPage> SearchAsync(string query, string? gameVersion = null,
        string projectType = "modpack", string? loader = "fabric", int offset = 0, CancellationToken ct = default)
    {
        var facets = new List<string[]> { new[] { "project_type:" + projectType } };
        if (!string.IsNullOrEmpty(gameVersion)) facets.Add(["versions:" + gameVersion]);
        if (!string.IsNullOrEmpty(loader)) facets.Add(["categories:" + loader]);
        var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}&limit=20&offset={offset}&index=relevance";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        var projects = new List<ModrinthProject>();
        foreach (var hit in doc.RootElement.GetProperty("hits").EnumerateArray())
            projects.Add(new(GetString(hit, "project_id")!, GetString(hit, "slug") ?? "", GetString(hit, "title") ?? "",
                GetString(hit, "description") ?? "", GetString(hit, "icon_url"), Strings(hit, "versions"), hit.GetProperty("downloads").GetInt64()));
        return new(projects, doc.RootElement.GetProperty("total_hits").GetInt32(), offset);
    }

    public async Task<List<ModrinthVersion>> GetVersionsAsync(string projectId, string? gameVersion,
        string projectType = "modpack", string? loader = "fabric", CancellationToken ct = default)
    {
        var filters = new List<string>();
        if (!string.IsNullOrEmpty(gameVersion)) filters.Add("game_versions=" + EncodeArray(gameVersion));
        if (!string.IsNullOrEmpty(loader)) filters.Add("loaders=" + EncodeArray(loader));
        var url = $"{BaseUrl}/project/{Uri.EscapeDataString(projectId)}/version?{string.Join('&', filters)}";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        var versions = new List<ModrinthVersion>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (gameVersion != null && !Strings(element, "game_versions").Contains(gameVersion)) continue;
            if (loader != null && !Strings(element, "loaders").Contains(loader)) continue;
            var version = ParseVersion(element, projectType);
            if (version != null) versions.Add(version);
        }
        return versions;
    }

    public async Task<ModrinthVersion> GetVersionAsync(string id, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"{BaseUrl}/version/{Uri.EscapeDataString(id)}", ct));
        return ParseVersion(doc.RootElement, "mod") ?? throw new InvalidDataException("Dependency has no downloadable JAR.");
    }

    private static ModrinthVersion? ParseVersion(JsonElement version, string type)
    {
        var extension = type == "modpack" ? ".mrpack" : ".jar";
        var files = version.GetProperty("files").EnumerateArray()
            .Where(f => (GetString(f, "filename") ?? "").EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.TryGetProperty("primary", out var primary) && primary.GetBoolean()).ToList();
        if (files.Count == 0) return null;
        var file = files[0];
        var name = GetString(file, "filename")!;
        if (name.Contains('/') || name.Contains('\\')) throw new InvalidDataException("Invalid download filename.");
        VerifiedDownload.ContainedPath(Path.GetTempPath(), name);
        var hashes = file.GetProperty("hashes");
        var dependencies = new List<ModrinthDependency>();
        if (version.TryGetProperty("dependencies", out var deps))
            foreach (var dep in deps.EnumerateArray())
                dependencies.Add(new(GetString(dep, "project_id"), GetString(dep, "version_id"), GetString(dep, "dependency_type") ?? ""));
        return new(GetString(version, "id")!, GetString(version, "project_id")!, GetString(version, "version_number")!,
            Strings(version, "game_versions"), Strings(version, "loaders"), GetString(file, "url")!, name,
            GetString(hashes, "sha512"), GetString(hashes, "sha1"), dependencies.ToArray());
    }
    private static string? GetString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string[] Strings(JsonElement element, string name) => element.TryGetProperty(name, out var values) ? values.EnumerateArray().Select(v => v.GetString()!).ToArray() : [];
}

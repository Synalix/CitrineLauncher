using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CitrineLauncher.Handlers
{
    public class MinecraftSkin
    {
        [JsonPropertyName("id")]      public string Id      { get; set; } = "";
        [JsonPropertyName("state")]   public string State   { get; set; } = "";
        [JsonPropertyName("url")]     public string Url     { get; set; } = "";
        [JsonPropertyName("variant")] public string Variant { get; set; } = "CLASSIC";
    }

    public class MinecraftCape
    {
        [JsonPropertyName("id")]    public string Id    { get; set; } = "";
        [JsonPropertyName("state")] public string State { get; set; } = "";
        [JsonPropertyName("url")]   public string Url   { get; set; } = "";
        [JsonPropertyName("alias")] public string Alias { get; set; } = "";
    }

    public class MinecraftProfile
    {
        [JsonPropertyName("id")]    public string Id   { get; set; } = "";
        [JsonPropertyName("name")]  public string Name { get; set; } = "";
        [JsonPropertyName("skins")] public List<MinecraftSkin> Skins { get; set; } = new();
        [JsonPropertyName("capes")] public List<MinecraftCape> Capes { get; set; } = new();
    }

    public static class SkinApiHandler
    {
        private const string ProfileUrl = "https://api.minecraftservices.com/minecraft/profile";
        private const string SkinsUrl   = "https://api.minecraftservices.com/minecraft/profile/skins";
        private const string CapesUrl   = "https://api.minecraftservices.com/minecraft/profile/capes/active";

        private static readonly HttpClient _http = new();

        private static HttpRequestMessage AuthRequest(HttpMethod method, string url, string accessToken)
        {
            var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return req;
        }

        public static async Task<MinecraftProfile> GetProfileAsync(
            string accessToken, CancellationToken ct = default)
        {
            using var req = AuthRequest(HttpMethod.Get, ProfileUrl, accessToken);
            var response = await _http.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<MinecraftProfile>(json)
                ?? throw new InvalidOperationException("Empty profile response");
        }

        public static async Task UploadSkinAsync(
            string accessToken, string filePath, string model, CancellationToken ct = default)
        {
            var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");

            using var form = new MultipartFormDataContent();
            form.Add(fileContent, "file", Path.GetFileName(filePath));
            form.Add(new StringContent(model.ToLower()), "variant");

            using var req = AuthRequest(HttpMethod.Post, SkinsUrl, accessToken);
            req.Content = form;
            var response = await _http.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
        }

        public static async Task<byte[]> DownloadSkinBytesAsync(
            string skinUrl, CancellationToken ct = default)
        {
            return await _http.GetByteArrayAsync(skinUrl, ct);
        }

        public static async Task SetActiveCapeAsync(
            string accessToken, string capeId, CancellationToken ct = default)
        {
            var body = JsonSerializer.Serialize(new { capeId });
            using var req = AuthRequest(HttpMethod.Put, CapesUrl, accessToken);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _http.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
        }

        public static async Task DisableCapeAsync(
            string accessToken, CancellationToken ct = default)
        {
            using var req = AuthRequest(HttpMethod.Delete, CapesUrl, accessToken);
            var response = await _http.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
        }
    }
}

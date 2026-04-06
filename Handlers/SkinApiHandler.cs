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

        private static void ApplyAuth(string accessToken)
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        }

        public static async Task<MinecraftProfile> GetProfileAsync(
            string accessToken, CancellationToken ct = default)
        {
            ApplyAuth(accessToken);
            var response = await _http.GetAsync(ProfileUrl, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<MinecraftProfile>(json)
                ?? throw new InvalidOperationException("Empty profile response");
        }

        public static async Task UploadSkinAsync(
            string accessToken, string filePath, string model, CancellationToken ct = default)
        {
            ApplyAuth(accessToken);
            using var form = new MultipartFormDataContent();
            var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(fileContent, "file", Path.GetFileName(filePath));
            form.Add(new StringContent(model.ToUpper()), "variant");
            var response = await _http.PostAsync(SkinsUrl, form, ct);
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
            ApplyAuth(accessToken);
            var body = JsonSerializer.Serialize(new { capeId });
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _http.PutAsync(CapesUrl, content, ct);
            response.EnsureSuccessStatusCode();
        }

        public static async Task DisableCapeAsync(
            string accessToken, CancellationToken ct = default)
        {
            ApplyAuth(accessToken);
            var response = await _http.DeleteAsync(CapesUrl, ct);
            response.EnsureSuccessStatusCode();
        }
    }
}

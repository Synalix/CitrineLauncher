using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CitrineLauncher.Handlers
{
    /// <summary>
    /// Minimal Yggdrasil-compatible HTTP server used as the authlib-injector target.
    /// Serves skin textures for offline accounts so they appear in-game.
    /// Only the endpoints authlib-injector actually calls are implemented.
    /// </summary>
    public sealed class OfflineSkinServer : IDisposable
    {
        // Used only for the large JAR download — long timeout is appropriate here.
        private static readonly HttpClient _downloadHttp = new HttpClient
        {
            DefaultRequestHeaders = { { "User-Agent", "CitrineLauncher" } },
            Timeout = TimeSpan.FromSeconds(120)
        };

        // Used for in-game skin texture fetches — short timeout to avoid blocking gameplay.
        private static readonly HttpClient _http = new HttpClient
        {
            DefaultRequestHeaders = { { "User-Agent", "CitrineLauncher" } },
            Timeout = TimeSpan.FromSeconds(10)
        };

        // ---- authlib-injector download ----
        private const string AutobuildUrl =
            "https://github.com/yushijinhun/authlib-injector/releases/download/v1.2.7/authlib-injector-1.2.7.jar";

        public static string JarPath => Path.Combine(
            Settings.DefaultMinecraftPath, "authlib-injector.jar");

        public static async Task EnsureJarAsync(CancellationToken ct = default)
        {
            if (File.Exists(JarPath)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(JarPath)!);
            var tmpPath = JarPath + ".tmp";
            try
            {
                var bytes = await _downloadHttp.GetByteArrayAsync(AutobuildUrl, ct);
                await File.WriteAllBytesAsync(tmpPath, bytes, ct);
                File.Move(tmpPath, JarPath, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmpPath); } catch { }
                throw;
            }
        }

        // ---- shared singleton ----
        public static readonly OfflineSkinServer Shared = new();

        // ---- server state ----
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Dictionary<string, Account> _byUuid = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public int Port { get; }
        public string BaseUrl => $"http://localhost:{Port}";

        public OfflineSkinServer()
        {
            Port = FindFreePort();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _ = RunAsync(_cts.Token);
        }

        /// <summary>Register (or update) an offline account so its skin is served.</summary>
        public void Register(Account account)
        {
            var uuid = account.GetOrCreateOfflineUuid();
            lock (_byUuid) { _byUuid[uuid] = account; }
        }

        /// <summary>Remove an account from the server (e.g. after it's deleted).</summary>
        public void Unregister(Account account)
        {
            if (!string.IsNullOrEmpty(account.OfflineUuid))
                lock (_byUuid) { _byUuid.Remove(account.OfflineUuid); }
        }

        // ---- request handling ----
        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(async () =>
                {
                    try { await HandleAsync(ctx); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"HandleAsync error: {ex.Message}"); }
                }, ct);
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            var req  = ctx.Request;
            var resp = ctx.Response;
            try
            {
                var path = req.Url?.AbsolutePath ?? "/";

                // GET / — API metadata (authlib-injector reads this first)
                // signaturePublickey is intentionally omitted — an empty string causes authlib-injector
                // to abort during startup because it tries to parse it as a PEM key.
                if (path == "/" || path == string.Empty)
                {
                    await WriteJsonAsync(resp, new
                    {
                        meta = new { serverName = "CitrineLauncher" },
                        skinDomains = new[] { "localhost" }
                    });
                    return;
                }

                // GET /sessionserver/session/minecraft/profile/<uuid>
                if (path.StartsWith("/sessionserver/session/minecraft/profile/", StringComparison.OrdinalIgnoreCase))
                {
                    var uuid = path["/sessionserver/session/minecraft/profile/".Length..].TrimEnd('/');
                    Account? account;
                    lock (_byUuid) { _byUuid.TryGetValue(uuid, out account); }

                    if (account == null || string.IsNullOrEmpty(account.SkinPath) || !File.Exists(account.SkinPath))
                    {
                        resp.StatusCode = 204;
                        resp.Close();
                        return;
                    }

                    var texturesJson = BuildTexturesJson(account, uuid);
                    var texturesB64  = Convert.ToBase64String(Encoding.UTF8.GetBytes(texturesJson));

                    await WriteJsonAsync(resp, new
                    {
                        id   = uuid,
                        name = account.Username,
                        properties = new[]
                        {
                            new { name = "textures", value = texturesB64 }
                        }
                    });
                    return;
                }

                // GET /skins/<username>.png — serve the raw skin PNG
                if (path.StartsWith("/skins/", StringComparison.OrdinalIgnoreCase))
                {
                    var file = Uri.UnescapeDataString(path["/skins/".Length..]);
                    Account? account;
                    lock (_byUuid)
                    {
                        account = _byUuid.Values.FirstOrDefault(a =>
                            string.Equals(Path.GetFileName(a.SkinPath), file, StringComparison.OrdinalIgnoreCase));
                    }

                    if (account == null || string.IsNullOrEmpty(account.SkinPath) || !File.Exists(account.SkinPath))
                    {
                        resp.StatusCode = 404;
                        resp.Close();
                        return;
                    }

                    resp.ContentType = "image/png";
                    var bytes = await File.ReadAllBytesAsync(account.SkinPath);
                    resp.ContentLength64 = bytes.Length;
                    await resp.OutputStream.WriteAsync(bytes);
                    resp.Close();
                    return;
                }

                // POST /api/profiles/minecraft — bulk username-to-profile lookup used by
                // authlib-injector's legacy skin polyfill before it fetches the profile endpoint.
                if (path.Equals("/api/profiles/minecraft", StringComparison.OrdinalIgnoreCase)
                    && req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
                {
                    using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                    var body = await reader.ReadToEndAsync();
                    string[] requestedNames;
                    try { requestedNames = JsonSerializer.Deserialize<string[]>(body) ?? Array.Empty<string>(); }
                    catch { requestedNames = Array.Empty<string>(); }

                    var results = new List<object>();
                    lock (_byUuid)
                    {
                        foreach (var name in requestedNames)
                        {
                            var match = _byUuid.Values.FirstOrDefault(a =>
                                string.Equals(a.Username, name, StringComparison.OrdinalIgnoreCase));
                            if (match != null)
                                results.Add(new { id = match.GetOrCreateOfflineUuid(), name = match.Username });
                        }
                    }
                    await WriteJsonAsync(resp, results);
                    return;
                }

                resp.StatusCode = 404;
                resp.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OfflineSkinServer: {ex.Message}");
                try { resp.StatusCode = 500; resp.Close(); } catch { }
            }
        }

        private string BuildTexturesJson(Account account, string uuid)
        {
            var skinFileName = Path.GetFileName(account.SkinPath);
            var skinUrl      = $"{BaseUrl}/skins/{Uri.EscapeDataString(skinFileName)}";
            var model        = account.SkinModel == "slim" ? "slim" : "default";
            var ts           = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // model metadata only needed for slim; omit for default to keep response minimal
            object skinEntry = model == "slim"
                ? new { url = skinUrl, metadata = new { model = "slim" } }
                : (object)new { url = skinUrl };

            var obj = new
            {
                timestamp   = ts,
                profileId   = uuid,
                profileName = account.Username,
                textures    = new Dictionary<string, object> { ["SKIN"] = skinEntry }
            };
            return JsonSerializer.Serialize(obj);
        }

        private static async Task WriteJsonAsync(HttpListenerResponse resp, object data)
        {
            var json  = JsonSerializer.Serialize(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentType     = "application/json; charset=utf-8";
            resp.ContentLength64 = bytes.Length;
            await resp.OutputStream.WriteAsync(bytes);
            resp.Close();
        }

        private static int FindFreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _listener.Stop();
            _listener.Close();
            _cts.Dispose();
        }
    }
}

using System;
using System.IO;
using System.Reflection;

namespace CitrineLauncher.Handlers
{
    public static class SkinViewerHtml
    {
        private static readonly Lazy<string> _js = new(LoadJs);
        private static readonly Lazy<string> _html = new(Build);

        private static string LoadJs()
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string resourceName = "CitrineLauncher.Assets.skinview3d.min.js";
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        // Default Steve skin — 64×64 RGBA PNG with correct Minecraft UV layout,
        // embedded as a base64 data URL so the fallback works without network access.
        private const string SteveSkinDataUrl =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAA60lEQVR42u3ZMQrCMBSA4ZzBK7iIoyCiuLmLq97ApXMP0UO4uXoKr/XEIZAUTFqToi/5f3hDaVPSb+gSYyJd9gsJjSk9AAAAAAAAACgYwH7I89Z6E/vw/ny7HgANAP17Q54Zul4FgM3es1UFsD7fPYD3dRUAnzYfew6AUn6CxQPsjp24M3Z9DCh1f7nfBwAAAOQFOFwfEprU/eV+HwAAAOC33DbiDgAAAFAXwGY+E3fGrl+dOgnNr/cHAAAAEBERERERERGZvz5cBQAAAOoCmPzgAwAAAJi03IerAAAAgK5SDy/VH34CAIBugBdVOAQjOMARKAAAAABJRU5ErkJggg==";

        public static string Build() => $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>
              * { margin: 0; padding: 0; box-sizing: border-box; }
              body { background: #0D0D0D; display: flex; justify-content: center;
                     align-items: center; height: 100vh; overflow: hidden; }
              canvas { display: block; }
            </style>
            </head>
            <body>
            <canvas id="skin_canvas"></canvas>
            <script>{{_js.Value}}</script>
            <script>
              var STEVE_SKIN = "{{SteveSkinDataUrl}}";
              var viewer = new skinview3d.SkinViewer({
                canvas: document.getElementById("skin_canvas"),
                width: window.innerWidth,
                height: window.innerHeight
              });
              viewer.animation = new skinview3d.WalkingAnimation();
              viewer.controls.enabled = true;
              window.addEventListener("resize", function() {
                viewer.setSize(window.innerWidth, window.innerHeight);
              });
              function setSkin(url, model) {
                viewer.loadSkin(url, { model: model || "default" });
              }
              function setCape(url) { viewer.loadCape(url); }
              function clearCape() { viewer.loadCape(null); }
              function loadDefault() {
                viewer.loadSkin(STEVE_SKIN, { model: "default" });
              }
            </script>
            </body>
            </html>
            """;

        public static string WriteTempFile()
        {
            var path = Path.Combine(Path.GetTempPath(), "citrine_skin_viewer.html");
            File.WriteAllText(path, _html.Value);
            return path;
        }
    }
}


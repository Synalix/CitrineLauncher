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
                viewer.loadSkin(
                  "https://crafatar.com/skins/8667ba71b85a4004af54457a9734eed7"
                );
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


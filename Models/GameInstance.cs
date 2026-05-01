using System;
using System.Text.Json.Serialization;

namespace CitrineLauncher.Models
{
    public enum LoaderType
    {
        Vanilla,
        Fabric
    }

    public class GameInstance
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Instance";
        public string GameVersion { get; set; } = string.Empty;
        public LoaderType Loader { get; set; } = LoaderType.Vanilla;
        public string LoaderVersion { get; set; } = string.Empty;

        // Relative to the instances root folder — set by InstanceManager
        [JsonIgnore]
        public string InstanceDirectory { get; set; } = string.Empty;

        // The version string CmlLib should install/launch
        // For Vanilla: GameVersion. For Fabric: "fabric-loader-<LoaderVersion>-<GameVersion>"
        [JsonIgnore]
        public string ResolvedVersion => Loader == LoaderType.Fabric && !string.IsNullOrEmpty(LoaderVersion) && !string.IsNullOrEmpty(GameVersion)
            ? $"fabric-loader-{LoaderVersion}-{GameVersion}"
            : GameVersion;

        public override string ToString() => Name;
    }
}

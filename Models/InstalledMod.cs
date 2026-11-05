namespace CitrineLauncher.Models;

public record ModIncompatibility(string? ProjectId, string? VersionId);

public record InstalledMod(string FileName, string Name, bool Enabled, string? ProjectId, string? VersionId, ModIncompatibility[]? Incompatibilities = null)
{
    public string State => Enabled ? "Enabled" : "Disabled";
    public string Source => ProjectId == null ? "Local / imported" : "Modrinth";
}

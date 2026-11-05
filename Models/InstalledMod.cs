namespace CitrineLauncher.Models;

public record InstalledMod(string FileName, string Name, bool Enabled, string? ProjectId, string? VersionId)
{
    public string State => Enabled ? "Enabled" : "Disabled";
    public string Source => ProjectId == null ? "Local / imported" : "Modrinth";
}

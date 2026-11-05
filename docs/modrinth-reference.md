# Modrinth implementation reference

Prism Launcher was cloned read-only at `/workspace/PrismLauncher-reference`, commit `059e858e101edc9314ccb94aa48bd01b282539c5`.

Studied:
- [ModrinthAPI.cpp](https://github.com/PrismLauncher/PrismLauncher/blob/059e858e101edc9314ccb94aa48bd01b282539c5/launcher/modplatform/modrinth/ModrinthAPI.cpp): search facets, game-version/loader filtering, dependency version endpoints.
- [ModrinthInstanceCreationTask.cpp](https://github.com/PrismLauncher/PrismLauncher/blob/059e858e101edc9314ccb94aa48bd01b282539c5/launcher/modplatform/modrinth/ModrinthInstanceCreationTask.cpp): manifest dependencies, client environment filtering, client override priority, hash validators and alternate download URLs.
- [ModFolderPage.cpp](https://github.com/PrismLauncher/PrismLauncher/blob/059e858e101edc9314ccb94aa48bd01b282539c5/launcher/ui/pages/instance/ModFolderPage.cpp): installed mods owned by an instance and a separate download dialog.

Prism is GPL-3.0. Citrine remains MIT; the implementation is original C# against the public Modrinth contracts, with no copied Prism source.

Public contracts: https://docs.modrinth.com/api/ and https://docs.modrinth.com/docs/modpacks/format_definition/ .

## Original failures addressed
- A running-search gate discarded subsequent searches instead of cancelling/replacing them.
- Pack browse was available only after selecting Fabric and incorrectly inherited the manual Minecraft-version filter.
- Creation picked the newest Fabric loader instead of the pack's declared loader version.
- Import downloaded only `mods/`, ignored client environment and client overrides, and did not validate checksums.
- Failed imports still closed with a partially created instance.

## Boundaries
Fabric/Vanilla only; no Forge, Quilt, NeoForge or CurseForge API support. No automatic mod upgrades. Imported/local JARs show filenames; metadata for Citrine-installed Modrinth mods is retained in citrine-mods.json. No background metadata scan is promised.

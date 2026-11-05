# Citrine Launcher

A simple Minecraft launcher built with Avalonia UI and CmlLib.Core.

It is meant to stay light and easy to use:

- launch Minecraft from a clean custom UI
- add offline or Microsoft accounts
- switch versions and manage multiple instances
- import Modrinth modpacks
- choose a Minecraft folder
- change basic launcher settings
- keep the look simple and readable

## Screenshots

![Main menu](Assets/screenshots/main-menu.png)
![Accounts and settings](Assets/screenshots/settings.png)

## Requirements

- Windows 10 or Windows 11
- .NET 10 SDK if you want to build it yourself

## Download

Grab the [latest release](https://github.com/Synalix/CitrineLauncher/releases/latest) and run `CitrineLauncher.exe`.

No installer for now.

## Build from source

Run these commands from the repository root.

```bash
dotnet build
```

To publish a self-contained build, run this from the repository root too:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Notes

- Settings are saved in `%AppData%\CitrineLauncher`
- Both offline and Microsoft accounts are supported. Microsoft login uses the standard OAuth device-code flow via CmlLib.
- Fabric loader and modpack import (CurseForge/Modrinth) are supported. Vanilla instances use per-instance game directories.

## License

[MIT](https://github.com/Synalix/CitrineLauncher/blob/main/LICENSE)

## Mod management

Select an instance and choose **Mods** to list installed JARs, add compatible Fabric mods from Modrinth (including required dependencies), add local JARs, enable/disable mods, or remove a selected mod. Configuration files and dependencies are retained when removing a mod. Existing files are not silently overwritten; remove an old version before installing another.

New Instance can browse Modrinth packs or select a local `.mrpack`. The pack manifest determines Minecraft and Fabric versions. Optional client files can be excluded. Unsupported loaders and CurseForge manifests report an error instead of creating an incomplete playable instance.

## Cloud verification

The .NET 10 SDK can cross-build the Windows app on Linux with `dotnet build` (Windows targeting is enabled in the project). Run portable service tests with `dotnet test tests/CitrineLauncher.Tests` and native headless UI checks with `dotnet test tests/CitrineLauncher.Headless.Tests`. The latter compiles the same application sources for its test host; it does not change the shipping Windows target.

Windows UI, NativeWebView, screen-reader and high-DPI testing are still required before merging. See [the design contract](DESIGN.md), [workflow contract](UX-CONTRACT.md), and [Modrinth reference notes](docs/modrinth-reference.md).

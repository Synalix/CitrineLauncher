# Citrine Launcher

A simple Minecraft launcher built with Avalonia UI and CmlLib.Core.

It is meant to stay light and easy to use:

- launch Minecraft from a clean custom UI
- add offline accounts
- switch versions
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
- The launcher is made for offline accounts right now

## License

[MIT](https://github.com/Synalix/CitrineLauncher/blob/main/LICENSE)

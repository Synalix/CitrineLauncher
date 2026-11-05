# Cloud validation — 2026-11-05

Branch: `feat/avalonia-accessibility-mod-management`. Shipping target stays `net10.0-windows` / `win-x64`. .NET SDK 10.0.403 installed in the cloud workspace.

## Automated evidence
- `dotnet test tests/CitrineLauncher.Tests`: 31 portable tests. Covers Modrinth filters, compatible/primary files, manifest versions, unsupported loaders, client environments, client override priority, path containment, hash/fallback downloads, transactional failures, local mod lifecycle, dependency cycles, order-independent exact dependency resolution and bidirectional/version-specific incompatibilities.
- `dotnet test tests/CitrineLauncher.Headless.Tests`: 7 native Avalonia tests compiled from the actual application AXAML/code. Covers constrained Settings/Mods/New Instance/Main layouts, long messages, safe confirmation focus, accessible field naming, late search responses, and Settings Escape handling.
- A stream test cancels after a partial file exists, confirms original destination content is retained, and confirms partial-file cleanup.
- Debug/Release Windows cross-builds and self-contained, single-file win-x64 publish succeeded. Generated executable: `/workspace/scratch/CitrineLauncher-win-x64/CitrineLauncher.exe`.
- `git diff --check`, targeted `dotnet format whitespace`, and official `designmd lint DESIGN.md` passed. Design lint: 0 errors, 0 warnings. Cloud-provided UI skill launcher/static-audit scripts were not installed as executable local files; no separate premium static-audit compliance claim is made.

## Live Modrinth smoke
Actual Citrine service code was invoked from a disposable console host; no downloaded JAR was executed. All temporary instances/downloads were deleted afterward.

- Queried Fabulously Optimized, project `1KVo5zza`.
- Selected pack `15.0.0-beta.3`: Minecraft `26.3`, Fabric `0.20.1`.
- Verified the `.mrpack` checksum, read its manifest and imported 49 declared client files. Final instance contained 101 files after overrides; optional client files were excluded in this smoke.
- Installed Mod Menu `11.0.5` for Minecraft `1.21.1` / Fabric. Three JARs were installed including required dependencies.
- Disabled, re-enabled, and removed Mod Menu successfully; dependency files remained.

## Independent review
A separate reviewer examined the whole feature diff. Two material findings were fixed and regression-tested:
1. Installed mods' incompatibility declarations were not checked in the reverse installation order. Declarations are now persisted (older managed metadata is fetched when needed), and checked in both directions.
2. Provisional newest dependency choices could reject valid exact constraints discovered later. The dependency closure is now rebuilt using exact constraints; obsolete provisional dependencies are discarded and contradictory exact constraints fail.

The original implementation was compiled separately against the regression tests: three relevant cases failed (incompatibility in both project/version-specific forms, and the unpinned-before-pinned diamond graph). All cases pass with the fixes. Mid-stream cancellation coverage also addresses the review's test-coverage observation.

## Implementation decisions and limits
- Used the explicitly requested feature branch in the clean cloud clone; no extra worktree. `main` was not changed or merged.
- Kept Vanilla/Fabric scope and independently implemented the public Modrinth contracts after studying Prism's source. Additional loader support needs its own launch/install implementation.
- Used a source-linked portable/headless test host to test in Linux while retaining the shipping Windows target. This does not establish Windows compatibility by itself.
- Kept current dark Citrine identity. Main-window text now inherits the bundled Inter font rather than relying on a platform-dependent family list.
- Local/imported mods are shown by filename; automatic metadata identification and automatic upgrades are outside this change.
- Structural improvements are verified; no measured startup/FPS/memory improvement is claimed.

## Remaining platform checks before merge
Windows 10/11 interaction: Microsoft authentication, Minecraft launch, NativeWebView skin preview, high-DPI sizing, keyboard traversal/popup selection, screen-reader names/live announcements and final visual comparison. Cross-builds cannot substitute for these checks.

Existing MicrosoftAuth/SkinsPanel nullability warnings remain. Parameterized dialog runtime-loader warnings remain where a context/content constructor is required; direct construction works in headless tests. The headless dependency restore also exposes the inherited Avalonia desktop transitive `Tmds.DBus.Protocol` 0.21.2 advisory (GHSA-xrw6-gwf8-vvr9), suppressed by the shipping project's pre-existing NU1903 setting. A dependency upgrade was not mixed into this UI/mod workflow change.

# Avalonia and Mod Management Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement task-by-task.

**Goal:** Improve existing Avalonia responsiveness/accessibility, repair Modrinth packs, and add instance mod management on an isolated feature branch.
**Architecture:** Shared AXAML styles and native Avalonia layout; an injectable Modrinth HTTP API and verified file downloader; staged pack and mod installs exposed through native dialogs.
**Tech Stack:** C#, .NET 10, Avalonia 11, Modrinth v2, xUnit with portable source-linked service tests.
**Spec:** ../specs/2026-11-05-avalonia-design.md plus the user's approved additions in this session.

## Global Constraints
- Preserve dark Citrine identity, existing Windows target, compiled bindings and account/launch semantics.
- Work on feat/avalonia-accessibility-mod-management; leave main unchanged.
- Fabric and Vanilla are the only supported loaders. Reject unsupported pack dependencies clearly.
- Implement independently; Prism Launcher GPL code is a reference, not copied source.
- Downloads require SHA-512/SHA-1 validation and safe contained paths. Never overwrite an unrelated mod silently.

## Review Focus
- Manifest version/loader mismatch must fail before publishing an instance.
- Cancelled or corrupt downloads leave no installed partial JAR/pack.
- Rapid searches and dialog closure must cancel/ignore outdated results.
- Required dependency cycles resolve once; unsupported dependency versions fail visibly.
- Long text and narrow sizes retain reachable actions, keyboard focus and readable errors.

### Task 1: Modrinth API and downloads
Files: Handlers/ModrinthClient.cs, Handlers/VerifiedDownload.cs, tests/CitrineLauncher.Tests/*, CitrineLauncher.csproj.
- [x] Add portable xUnit tests for query facets, version filtering, primary files, dependency parsing, safe paths and hash validation; run RED.
- [x] Implement injectable ModrinthApi with search/version/dependency endpoints and static default client facade.
- [x] Implement streamed, cancellation-aware, hash-verified atomic downloads and URL fallback; run GREEN.

### Task 2: Pack imports and mod services
Files: Handlers/ModpackImporter.cs, Handlers/InstanceModManager.cs, Models/InstalledMod.cs.
- [x] Add manifest, client environment, override ordering, traversal, failed import and dependency tests; run RED.
- [x] Use manifest Minecraft/Fabric versions, client file filtering, full declared paths, checksum validation and staging.
- [x] Add list/local-add/remove/enable mod services and recursive required dependency installs with conflict checks and rollback; run GREEN.

### Task 3: Mod browser and instance workflow
Files: ModrinthBrowseDialog.axaml(.cs), NewInstanceDialog.axaml(.cs), ModsDialog.axaml(.cs), MainWindow.axaml(.cs).
- [x] Replace search gate with cancellation + stale-result guards; initial results, load more and explicit version selection.
- [x] Separate pack browsing from manual Minecraft version; use manifest dependencies, recover from failures and clean temporary files.
- [x] Add selected-instance Mods action, installed list, local import, enable/disable, confirmed removal and compatible Modrinth installation.
- [x] Disable mutations during installation/game execution and provide cancel/loading/error/empty states.

### Task 4: Avalonia optimization and accessibility
Files: Styles/Shared.axaml, App.axaml, existing AXAML, Handlers/SettingsHandler.cs, DESIGN.md, UX-CONTRACT.md.
- [x] Consolidate identical styles with explicit scope; simplify account template; preserve virtualized list panels.
- [x] Center/constrain settings with layout; improve dialog sizing and long-text wrapping; add icon/field accessible names and keyboard defaults.
- [x] Ignore input on decorative layers; pause decorative generation when hidden/minimized and support disabled motion.
- [x] Add meaningful headless layout/search interaction tests if NativeWebView permits isolation.

### Task 5: Verification and handoff
- [x] Run portable service and headless tests, Debug/Release Windows cross-builds and win-x64 publish; inspect diff.
- [x] Record Prism source links/commit, exact test results, existing warnings and remaining Windows runtime validation.
- [ ] Obtain one independent whole-branch review, address material findings, commit the feature branch without merging.

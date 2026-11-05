# Citrine workflow contract

Visual conventions: [DESIGN.md](DESIGN.md).

| Capability | Canonical owner | Source of truth | Allowed variants | Verification |
| --- | --- | --- | --- | --- |
| Select/Listbox | Avalonia Fluent ComboBox/ListBox | Typed AXAML templates | Single catalog version, single mod selection; native keyboard/popup behavior | Headless selection/layout tests; Windows keyboard follow-up |
| Form | Native labeled/automation-named fields | NewInstanceDialog, SettingsPanel | Manual instance or manifest-owned versions | AXAML build and constrained layout tests |
| Scrollbar | Avalonia Fluent ScrollViewer | Bounded lists and dialog body grids | Vertical content scrolling | Headless layout; Windows rendering follow-up |
| CRUD | InstanceModManager, PackInstaller, InstanceManager | Per-instance files and metadata | New instance, local/remote mods, enable/disable, confirmed remove | Portable service tests |
| Feedback | Inline TextBlock status/errors | Owning dialog | Polite progress/status; assertive validation | Headless state checks; Windows screen-reader follow-up |

## Instance and mod lifecycle
Pack selection owns Minecraft and loader versions, taken from modrinth.index.json. Citrine supports Vanilla and Fabric, and rejects unsupported loaders with an explanation. Pack browsing starts without the manual Minecraft filter and shows Fabric packs. Local Modrinth packs may also be Vanilla. CurseForge manifest downloads are not implemented and must never appear successful after copying only overrides.

Creation stays open on failure, removes the newly created incomplete instance, and preserves the selection for retry. Cancel during creation requests cancellation; close again after cleanup. Downloads use staged files and verified hashes. No incomplete JAR is published on download failure or cancellation.

Mods belongs to the currently selected instance. Vanilla cannot install JAR mods. Modrinth search/version selection filters by the instance's Minecraft version and Fabric. Required dependencies are included recursively and conflicting versions fail visibly. Optional/recommended dependencies are not installed automatically. Pack optional client files have an explicit opt-in checkbox (enabled by default).

Existing mod files are never overwritten silently. To change a mod version, remove its existing file first. Disabled mods retain their file with .disabled appended. Removal confirms the selected file and retains dependencies/configuration. Local/imported mods are listed by filename; no unsupported claim of metadata discovery.

## Async and recovery
Search is debounced; explicit Search/Enter runs immediately. Clear resets results immediately. Superseded requests are cancelled and stale results ignored. Load more extends the current query. Dialog closure cancels owned network work. Download operations disable duplicate actions and expose Cancel. Errors stay inline with retry guidance; no swallowed import failure or forced successful close.

Do not change mods or delete an instance while its Minecraft launch/process is active. Modal mod management prevents launching while edits run. Installation cancellation is cooperative; publication occurs only after complete downloads and validation.

## Accessibility
Use native focusable controls and keyboard selection. Name icon-only actions and selectors via AutomationProperties.Name. Wrap errors and status text; expose progress through live settings. Confirmation focuses Cancel. Keep dialog actions reachable at minimum supported size. Runtime WCAG equivalence, Windows screen-reader announcements, popup geometry and high-DPI interaction need real platform validation.

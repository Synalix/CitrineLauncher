# Compact categorized workspace

Goal: replace spacious secondary windows with one compact in-window workspace, following the user's explicit Prism-style direction. Preserve the main launcher view and the validated mod/download services.

- [x] Add a single ManagementWorkspace with native category navigation, a shared page host and instance context.
- [x] Convert New Instance, Mods and Modrinth browsing from Windows to embedded UserControls. Use navigation events/results rather than nested modal windows. Keep creation state when browsing packs. Preserve cancellation and block leaving during mutations.
- [x] Move Settings into shell categories; replace broad cards/controls with compact labeled rows. Add/edit offline accounts inline. Integrate Skins with the same compact density.
- [x] Route the main-window actions into the workspace; keep launcher layout. Block launch/mutations appropriately and restore focus when returning.
- [x] Add headless navigation/lifecycle/compact-layout tests, preserve service suite, inspect screenshots at normal/minimum sizes, cross-build/publish and independently review.

Layout: 132 DIP category column, 36 DIP workspace header, 12 DIP content inset, 26–28 DIP controls. Form fields have useful widths (roughly 120–260 DIP); action buttons size to content. Lists retain bounded scrolling/virtualization. Destructive confirmations and OS file pickers remain compact native dialogs; management tasks do not create windows.

Branch: continue feat/avalonia-accessibility-mod-management. Keep main unchanged. User request supplies the layout direction; implement directly.

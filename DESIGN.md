---
version: alpha
colors:
  background: '#121212'
  surface: '#1A1A1A'
  dialog: '#1E1E1E'
  card: '#222222'
  control: '#2A2A2A'
  text: '#FFFFFF'
  secondary: '#CCCCCC'
  muted: '#B0B0B0'
  primary: '#E2B007'
  accentStart: '#E2B007'
  accentEnd: '#9ACD32'
  danger: '#FF6B6B'
typography:
  sans:
    fontFamily: 'Inter, Segoe UI, sans-serif'
rounded:
  control: '4px'
  card: '5px'
omitted:
  - section: spacing
    reason: 'Existing native layouts use explicit device-independent values, rather than a generated spacing scale.'
  - section: components
    reason: 'Avalonia Fluent owns control templates; semantic class styles are maintained in AXAML.'
---

## Overview
Citrine is a Windows Minecraft launcher for selecting accounts and isolated Vanilla/Fabric instances. Preserve its existing dark desktop identity. The gold-to-green Citrine gradient is its signature; task screens should stay restrained and readable.

## Colors
App.axaml owns CitrineBrush and CitrineHover. Styles/Shared.axaml owns reusable semantic actions; Styles/Compact.axaml owns management density. ManagementWorkspace includes that density once for all embedded pages. Screen-specific surfaces remain local AXAML. This document describes those runtime owners; no CSS or generated web tokens are used.
White and #CCCCCC carry text; #B0B0B0 is used for catalog metadata on dark surfaces. Destructive actions require text and confirmation, not color alone. Retain Fluent focus visuals and add a visible accent border to shared buttons when focused.

## Typography
Use the bundled Inter font configured by Program.WithInterFont. The main window inherits that configuration instead of declaring a platform-dependent font-family list. Management headings are 14 DIP; labels and list content are 12 DIP, with 11 DIP secondary metadata. Preserve the existing main-window wordmark. Wrap errors and explanatory text; ellipsize long filenames/titles in bounded list cells with full filenames in tooltips.

## Layout
Native desktop, English content, Windows 10/11. The main launcher view retains its existing layout. Secondary tasks share one in-window ManagementWorkspace with a 132 DIP category rail, 36 DIP header and 12 DIP content inset. Instances, New instance, Mods, Modrinth, Accounts, Skins, General, Appearance and Performance are categories, not separate task windows.
Controls are 26–28 DIP high with content-sized actions. Typical form fields are 110–240 DIP wide, and labels sit beside inputs. Avoid stretched settings rows, large title banners and nested padded cards. Keep lists bounded and scrollable. Creation stays naturally sized at the top of its pane, with scrolling when constrained.
The workspace preserves the creation form while browsing packs. Only destructive confirmation, OS file pickers and provider authentication use separate native dialogs.

## Elevation & Depth
Flat dark surfaces and fine borders distinguish areas. No new blur/shadow effects.

## Shapes
Use 4 DIP management controls and a 5 DIP workspace frame. Do not add a separate rounded card around every option. Native Fluent templates own popup geometry and keyboard behavior.

## Components
Compact density is scoped to ManagementWorkspace so the main launcher does not inherit it. Modrinth and installed-mod results use typed compiled templates. Reuse ConfirmDialog for destructive operations; it focuses Cancel first. Use native ComboBox/ListBox rather than custom popup implementations.
Decorative particles ignore input, stay clipped, and stop generating while the launcher is hidden/minimized or covered by the management workspace. The existing particle preference provides a motion-off option.

## Do's and Don'ts
The user explicitly replaced the spacious secondary-screen convention with compact Prism-style category navigation. Preserve account identity, download integrity and the main launcher view. Expose accessible names for icon-only controls and selectors. Show progress, empty results, actionable errors and cancellation. Do not claim measured rendering gains from style deduplication or headless tests. Validate NativeWebView, DPI, screen readers and final appearance on Windows before merging.

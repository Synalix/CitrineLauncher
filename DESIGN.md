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
  control: '8px'
  card: '10px'
omitted:
  - section: spacing
    reason: 'Existing native layouts use explicit device-independent values, rather than a generated spacing scale.'
  - section: components
    reason: 'Avalonia Fluent owns control templates; semantic class styles are maintained in AXAML.'
---

## Overview
Citrine is a Windows Minecraft launcher for selecting accounts and isolated Vanilla/Fabric instances. Preserve its existing dark desktop identity. The gold-to-green Citrine gradient is its signature; task screens should stay restrained and readable.

## Colors
App.axaml owns CitrineBrush and CitrineHover. Styles/Shared.axaml owns reusable primary/secondary actions, cards, section titles and labels. Screen-specific surfaces remain local AXAML. This document describes those runtime owners; no CSS or generated web tokens are used.
White and #CCCCCC carry text; #B0B0B0 is used for catalog metadata on dark surfaces. Destructive actions require text and confirmation, not color alone. Retain Fluent focus visuals and add a visible accent border to shared buttons when focused.

## Typography
Use the bundled Inter font configured by Program.WithInterFont. The main window inherits that configuration instead of declaring a platform-dependent font-family list. Primary headings are 18–20 DIP; labels and list content 12–14 DIP. Preserve the existing main-window wordmark. Wrap errors and explanatory text; ellipsize long filenames/titles in bounded list cells with full filenames in tooltips.

## Layout
Native desktop, English content, Windows 10/11. Main window supports 720×540 DIP and above, with wrapping actions and a separate progress row. Settings is constrained by its host, not positioned by timed margin calculations. New dialogs resize with bounded list/scroll areas and pinned actions. Keep native ListBox virtualization and scrollbar behavior.

## Elevation & Depth
Flat dark surfaces and fine borders distinguish areas. No new blur/shadow effects.

## Shapes
Preserve existing 6–8 DIP control radii, 10 DIP cards and 16 DIP settings panel. Native Fluent templates own popup geometry and keyboard behavior.

## Components
Semantic classes are explicitly included by Settings, Skins and Mods. Modrinth and installed-mod results use typed compiled templates. Reuse ConfirmDialog for destructive operations; it focuses Cancel first. Use native ComboBox/ListBox rather than custom popup implementations.
Decorative particles ignore input, stay clipped, and stop generating while the launcher is hidden/minimized or covered by Settings/Skins. The existing particle preference provides a motion-off option.

## Do's and Don'ts
Preserve copy and account behavior outside this feature. Expose accessible names for icon-only controls and selectors. Show progress, empty results, actionable errors and cancellation. Do not claim measured rendering gains from style deduplication or headless tests. Validate NativeWebView, DPI, screen readers and final appearance on Windows before merging.

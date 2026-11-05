# Avalonia AXAML optimization proposal

Scope: all eight existing AXAML surfaces and the directly related layout code.
Preserve Citrine's dark palette, gradients, copy, account behavior, instance behavior, and Windows target.

## Approaches
1. Recommended: optimize the existing implementation. Consolidate identical styles, simplify repeated templates, fix constrained layout and decorative input handling, and preserve compiled binding coverage. This minimizes behavioral and visual risk.
2. Redesign and optimize together. Allows larger changes to navigation and screen composition, but needs a new visual brief and broader Windows UI validation.
The user approved the appearance-preserving approach and explicitly requested responsive/accessibility improvements plus instance mod management.

## Proposed changes
- Extract identical Settings/Skins primary and secondary actions, cards, section headings, and labels into one explicitly scoped shared Styles resource. Keep differing control states local; preserve current padding and appearance.
- Keep compiled bindings enabled and retain typed DataTemplates. Review binding modes against code-behind before altering them; do not convert editable settings to OneTime.
- Reduce the account ComboBox's panel/text-element tree using inline Runs while preserving colors and spacing. Review other repeated templates for redundant wrappers. Retain the built-in virtualizing ListBox panel rather than replacing it with StackPanel.
- Let Avalonia center and constrain the Settings panel instead of computing margins after a timed delay. Remove redundant initial transforms that do not have a configured animation. Ensure panel content remains scrollable at the supported minimum window size.
- Bound the main window's minimum size around its existing bottom controls; constrain and trim long status/account/instance labels so they cannot obscure actions.
- Make decorative particle and branding layers ignore hit testing and clip particles to their content region. Review animation lifetime and hidden/minimized behavior before changing animation code.
- Improve dialog content sizing and error wrapping so Fabric controls and error messages do not overlap footer actions.
- Add accessible names to icon-only controls and labels to selectors without changing visible copy.
- Record the incumbent visual conventions in DESIGN.md and document Linux cross-build commands.

## Verification
- Baseline: .NET SDK 10.0.403 installed; dotnet build -p:EnableWindowsTargeting=true succeeds, 0 errors, 9 warnings.
- After changes: Debug and Release cross-builds, AXAML compilation, diff whitespace checks, review all named controls/event handlers and typed templates.
- Add targeted layout tests if a runnable Avalonia headless harness is feasible; use meaningful constrained-size and long-content cases.
- Windows UI validation remains necessary for NativeWebView, input behavior, DPI scaling, dialog sizing, and rendering performance.
- Do not claim measured performance gains without a runtime profile. Style deduplication and fewer visual elements are structural improvements, not benchmark results.

## Existing warnings
Six C# nullability warnings in MicrosoftAuth/SkinsPanel, and three AVLN3001 runtime-loader constructor warnings for dialogs created using parameters. Investigate relevance before changing constructors or suppressing warnings.

## Approved additions
Work on a new feature branch because the user cannot test at home. Study Prism Launcher as a reference for Modrinth packs and mods. Add a per-instance installed mod list with Modrinth installs, local JARs, enable/disable and confirmed removal. Use the current Vanilla/Fabric loader scope and reject unsupported packs clearly. Do not merge into main before Windows validation.

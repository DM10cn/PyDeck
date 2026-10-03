# Settings workspace, 2026-10-03

## Scope

The settings workspace replaces the long settings page. Both presentations offer Appearance, Interface, Python management, Network and sources, Storage and logs, and About. Entering settings hides the main app navigation; Back returns to the previous destination. At less than 900 DIPs of workspace width, category selection and detail are separate pages. Wider windows show both columns. Category changes retain editor instances, unsaved drafts, and each category's scroll position; changing language or appearance rebuilds their presentation.

Material uses small accent section labels, continuous low-tonal rows (24 DIP outer / 4 DIP inner corners), pill category navigation, borderless tonal selectors and filled triangular disclosure icons. Fluent retains native list navigation and control styling. Native ComboBox popup, keyboard and accessibility behavior remain in use. High contrast retains visible boundaries.

## Shared preferences

- Startup destination: My Python, Install Python, Virtual environments, Build Python, Activity, Settings.
- Window close: exit, minimize to taskbar, or notification area. Existing active-work protection remains; the notification icon supports restore and explicit exit.
- System font: uses XAML's platform font. Off selects PyDeck's existing Segoe UI Variable preference; no external font is downloaded.
- OLED optimization: pure black shell/canvas in dark mode, retaining tonal cards and high-contrast overrides. Fluent backdrop selection is preserved while the backdrop is temporarily disabled.
- System title bar: applies on the next launch. On uses the native system frame; off removes both the native frame and the custom title area, including the icon, caption, and minimize/maximize/close buttons. Off preserves the window's size instead of changing display resolution or entering exclusive fullscreen. Settings → About → Exit PyDeck remains available in both designs.
- About: real PyDeck version, updates/releases, source, issues, MIT license and third-party notices.

## Interaction and persistence

Material ripple grows for 520 ms and fades for 280 ms. A short tap completes growth before fading, without delaying the button command. Switch press feedback is immediate, with a 100 ms thumb transition. Ordinary switch changes update their state in place and save on an ordered background queue; they do not rebuild the page. Real exit and restart flush pending saves. Language, theme and font changes still rebuild the affected presentation.

## Verification

- Core checks: `artifacts/settings-workspace-build.log`, **120 passed / 0 failed**.
- Settings UI: `artifacts/smoke-20261003-180047/result.json`, passed. Six categories across 16 layout/language scenarios, including 1680 DIP Chinese wide windows; 58 screenshots. Native selection/back navigation, narrow-screen focus, scroll/draft retention, real preference controls and consecutive OLED/titlebar input were checked.
- Ripple: `artifacts/smoke-20261003-180207/result.json`, passed. Animation timing, geometry, clipping, cleanup, reload and native UIA invocation were checked. This is not physical mouse/keyboard input latency measurement.
- Full UI regression: `artifacts/smoke-20261003-181127/result.json`, passed. Includes 192 page combinations, native selectors/expanders, design restart prompts, dynamic colors, four-language controls, runtime/activity workspaces, equal installation-action bounds, build forms and virtualized long-list behavior. The historical-release fixture now explicitly realizes and checks each group entry instead of assuming every offscreen row exists.
- Final build startup with `UseSystemTitleBar: true`: `artifacts/settings-native-titlebar-20261003-1815/initial-settings.json` and `result.json`, passed. Repeated the 16-scenario settings matrix and four-language catalog preview-switch checks on `dev-win-x64-20261003-181700`; the custom title bar was absent from the captured XAML root as expected. This verifies startup with the saved caption preference, not a manual native-caption interaction or restart cycle.
- The initial Material test exposed an invalid AnimatedIcon source mutation. The final arrow implementation leaves the SDK animated source and state targets intact and renders a non-interactive triangular PathIcon in the same slot. The complete settings matrix and native selector expansion/selection checks then passed.

Delivery directory and SHA-256 values are recorded in `artifacts/settings-delivery.json`. The installed copy is not replaced by this development build.

### Borderless correction

The 18:32 build corrects the meaning of disabling the system title bar: it now calls the native presenter to remove both the border and caption, and removes the custom title area in both modes. The setting description explains that the caption and all three window buttons disappear after restarting.

- Build: `artifacts/dev-win-x64-20261003-183205`, zero warnings/errors; `artifacts/window-chrome-build.log`.
- Core checks: `artifacts/window-chrome-core-checks.log`, **120 passed / 0 failed**.
- Native window checks: `artifacts/window-chrome-20261003-183239/result.json`, **4 passed** (Fluent/Material × title bar off/on). Separate processes load isolated saved preferences, check the actual HWND caption style and presenter border/title state, zero custom title-row height, resize/restore, deferred preference persistence, and the accessible About exit action. The initial fixture was adjusted to the wide layout because narrow category navigation deliberately gives keyboard focus to Back; all four final fixtures preserve foreground focus.
- This probe does not invoke Exit or Maximize and does not capture the native window frame. It verifies native window state, not exclusive fullscreen rendering or a manual desktop walkthrough.
- Corrected build path and hashes: `artifacts/window-chrome-delivery.json`.

### Material decorative borders

The user's final clarification requests removal of the thin decorative outline only, retaining existing button shapes, radii, fills and interaction. `artifacts/dev-win-x64-20261003-190351` applies this to search/text fields, action and installation-state buttons, design choices, switch tracks, settings expanders, color previews and native popup borders. The selector's duplicated focus halo is replaced with its single system keyboard focus ring. High-contrast boundaries are retained.

- Build succeeded with zero warnings/errors: `artifacts/material-border-cleanup-build.log`.
- Focused component regression passed: `artifacts/smoke-20261003-190427/result.json`. It covers both designs, light/dark, real search editing/filtering/clearing, both Python search locations, selector popup/selection, collapsed/expanded group states, component gallery across four languages, restart-dialog cancellation, and build-page controls. The initial extra search fixture incorrectly navigated to the empty virtual-environment page; the corrected fixture verifies the requested My Python and Install Python search controls.
- Rendered dark/light search surfaces, the Material button gallery and the selector's single keyboard focus ring were visually inspected. This was automated fixture/UIA validation, not a manual desktop input-method or high-contrast walkthrough.
- Build path, hashes and screenshot paths: `artifacts/material-border-cleanup-delivery.json`.

Automated UI checks do not replace user assessment of animation feel, notification-area lifecycle, startup-page/native-titlebar restart cycles, multiple monitors, or screen-reader usability. Those real desktop behaviors have not been manually accepted in this run. Theme/language/font changes rebuild the presentation; ordinary category navigation retains the cached editors.

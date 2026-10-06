# AGENTS.md

ElyProxy: WPF desktop client (.NET 8, `net8.0-windows`) for VLESS subscriptions on Windows. Runs Xray-core for SOCKS5/TUN and sing-box for the ElyTun full-tunnel mode. UI text is Russian, code comments are English. The folder is `SuperSocks`, but project/namespace/assembly is `ElyProxy`.

## Commands

- Build: `dotnet build ElyProxy.sln`
- Test from repo root: `dotnet test` (18 xunit tests, `tests/ElyProxy.Tests`)
- Focused test: `dotnet test --filter "FullyQualifiedName~SingBoxConfigBuilderTests"`
- Run: `dotnet run` — the client starts unelevated; selecting ElyTun triggers an on-demand UAC prompt and elevated relaunch.
- Local preview build (no installer): `dotnet publish ElyProxy.csproj -c Release -r win-x64 --self-contained false -o publish\redesign-preview`. `publish/` is gitignored; the csproj copies the repo-root `xray/**` runtime files into the output. A running `ElyProxy.exe` locks the folder — exit the app or publish to a new folder.
- Release: bump `Version`, `AssemblyVersion`, `FileVersion` in `ElyProxy.csproj`, then run `.\tools\New-ElyProxyUpdate.ps1 -Version X.Y.Z -ReleaseNotes '...'`. The script fails if the csproj version doesn't match or artifacts for that version already exist. Details in `tools/README.md`.

Publishing the main project also publishes `ElyProxy.Updater` into the `updater/` subfolder (MSBuild `PublishUpdater` target); the self-update flow depends on that layout. `ElyProxy.csproj` excludes `tests/**` and `ElyProxy.Updater/**` from its own compilation.

## Layout

- `Core/` — child-process + config generation. `XrayManager`/`SingBoxTunManager`/`SingBoxProxyManager` wrap `ProcessManager`; `ConfigBuilder` emits Xray JSON, `SingBoxConfigBuilder` emits sing-box JSON; `RoscomVpnDefaultRouting` is the bundled routing preset.
- `Services/` — storage (`%AppData%/ElyProxy/`), VLESS/subscription parsing, PAC HTTP server, Windows proxy registry changes, update check/download, autostart, port owner lookup, plus `ThemeService` (appearance) and `DialogService` (sheets/toasts).
- `ViewModels/MainViewModel.cs` (~100 KB, decompiled) holds essentially all app behavior/commands. It is `partial`; UI-only members live in `ViewModels/MainViewModel.Ui.cs`.
- `Views/MainWindow.xaml` is only the window shell (title bar, sidebar, status bar, log drawer). Screens are `Views/Sections/*.xaml` (servers, subscriptions, profiles, settings) and modal forms are `Views/Sheets/*.xaml`. There are four sections; `Ctrl+1..4` switch them.
- `Theme/` — the design system: `Tokens.Light.xaml`/`Tokens.Dark.xaml`/`Tokens.HighContrast.xaml` (swapped at runtime), `Typography.xaml`, `Converters.xaml`, `Controls/*.xaml` (buttons, inputs, lists, menus, surfaces, shell).
- `Controls/` — custom controls and the motion engine: `Motion/SpringAnimator.cs` (frame-driven springs), `Motion/MotionPreferences.cs` (reduced motion/transparency), `SegmentedControl`, `ModeSelector`, `SpringToggleSwitch`, `EmptyState`, `SkeletonList`, `BusyRing`, `SheetHost`, `ToastHost`, `Behaviors/SmoothScroll.cs`, `Converters/`.
- `Interop/` — `WindowChromeHelper.cs` (Snap Layouts hit-testing on Windows 11, caption dragging) and `WindowBackdropInterop.cs` (DWM Mica/Acrylic).
- `ElyProxy.Updater/` — separate WinForms exe that replaces files after the client exits and restarts it.
- `deployment/elyaxis-staging/` — unrelated Telegram bot / Laravel staging files, not part of the C# build.

## UI conventions (Apple-style redesign)

- Theming is fully ours: `ThemeService` swaps the whole token dictionary by reassigning `ResourceDictionary.Source`, reads the system theme from `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` + `SystemEvents.UserPreferenceChanged`, and picks `Tokens.HighContrast.xaml` when `SystemParameters.HighContrast` is set. WPF-UI's theme manager/watcher is deliberately not used.
- **WPF-UI 4.3.0 is still a `PackageReference` but nothing uses it any more** — no dictionaries are merged from it and no XAML references its controls (only a leftover unused `xmlns:ui` declaration in `MainWindow.xaml`). Don't reintroduce it as a theme/backdrop owner; it fights `ThemeService` over the window state.
- Every color/brush is a token referenced with `DynamicResource` — `StaticResource` on colors breaks live theme switching.
- Backdrop: `Interop/WindowBackdropInterop.Apply` uses `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` on Windows 11 and the composition accent (`SetWindowCompositionAttribute`) on Windows 10. It must run after the HWND exists (`ThemeService` hooks `SourceInitialized`), the window `Background` stays `Transparent`, and `ShellSurfaceBrush` toggles between `MaterialThinBrush` (translucent shell) and `Bg2Brush` (opaque) depending on the mode.
- Typography: WPF has no letter-spacing. Optical sizing is approximated with the `Segoe UI Variable Display`/`Text` variants plus `Typography.Kerning`; don't try to fake tracking with inserted spaces (it breaks copy and screen readers).
- `Radius.Card`/`Radius.Control`/`Radius.Small` are `CornerRadius`, not `Double` (using `Double` throws at load).
- A control derived from another custom control does **not** inherit its implicit style: `DefaultStyleKeyProperty.OverrideMetadata` does not propagate to derived types, so e.g. `ModeSelector` needs its own `Style TargetType="{x:Type controls:ModeSelector}"` (without it the control silently renders with zero height).
- A template child that is itself a `Control` takes `Foreground`/`Background` from its system default style, and that beats inherited values. Bind them explicitly in custom templates (`Foreground="{TemplateBinding Foreground}"` on the ComboBox's inner `ToggleButton`); otherwise e.g. ComboBox selection text stays system-default black in the dark theme.
- Motion: use `SpringAnimator.AnimateTo(...)` for anything gesture-driven or interruptible; storyboards are only for press feedback and short fades. Check `MotionPreferences.ReducedMotion` for opt-outs.
- Feedback: no `MessageBox` — use `DialogService.Current` (`ConfirmAsync`, `AlertAsync`, `Notify`). Both surfaces are our own controls: `Controls/SheetHost` (scrim + material card + spring) and `Controls/ToastHost` (top-right stack). WPF-UI's `ContentDialog`/`Snackbar` are not used at all. `DialogService` must be attached by `MainWindow` before use (it is, in the constructor).
- Scrolling: `Controls/Behaviors/SmoothScroll` is attached to the template/page `ScrollViewer`s and springs the wheel; lists set `ScrollViewer.CanContentScroll="False"` so offsets are pixels — keep that pairing when editing list styles.
- The tray menu is `Views/TrayMenuWindow` (own WPF popup on right-click, closes on deactivate, forces foreground via `SetForegroundWindow`).
- The three exclusive connection modes are surfaced as one `ModeSelector` bound to the existing `SystemProxyEnabled`/`ProxyProEnabled`/`ElyTunEnabled` flags; the view model already enforces exclusivity.
- The window uses a custom title bar (`WindowStyle="None"` + `WindowChrome`); the window `Icon` and tray icon use the explicit `pack://application:,,,/ElyProxy;component/icon.ico` URI so they resolve outside the entry assembly.
- The journal is filtered **in the view**: `LogBox.Text` is not bound to `LogText` (the view model keeps the full text, `MainWindow.xaml.cs` applies search/level filtering). Don't re-add the binding.
- Row context menus are declared in `UserControl.Resources` with `x:Shared="False"` and receive the view model via `Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=UserControl}}"` + `PlacementTarget.Tag`. Dropping `x:Shared` breaks reuse across rows.
- Sheets with inline validation implement `Services.ISheetValidation`; `Controls/SheetHost` binds its `PrimaryButton.IsEnabled` to `IsValid` (via `DialogService.ShowCustomAsync`), so the sheet cannot be submitted invalid.
- Loading placeholders: `Controls/SkeletonList` shown through the `BoolAndZeroToVis` multi-converter (`IsLoading` + collection count).
- High contrast: `Theme/Tokens.HighContrast.xaml` maps every token to `SystemColors`; `ThemeService` selects it when `SystemParameters.HighContrast` is true.

## Testing notes

- Tests instantiate builders directly (no mocks) and assert on generated JSON.
- `SingBoxConfigBuilderTests` shells out to `sing-box/sing-box.exe check` and locates the repo root by walking up to `ElyProxy.sln`, so tests fail if the committed `sing-box/sing-box.exe` is missing or the project is run from a copied output dir. They also assert the 22 bundled rule-sets under `Assets/RoscomVPN/` exist.
- No UI tests and no lint/format/typecheck tooling. UI changes are verified by building, then running the app (UAC) and checking: both themes, Mica/Acrylic fallback on Windows 10, reduced motion/transparency, sidebar sections, log drawer, sheets/toasts.
- Cheap pre-check without elevation: an STA smoke harness lives outside the repo at `%TEMP%\opencode\elyproxy-smoke` (`dotnet run --project ...\ElyProxySmoke.csproj`). It `ProjectReference`s `ElyProxy.csproj`, instantiates `ElyProxy.App`, and renders `MainWindow`, all sections, sheets, tray menu and controls off-screen. It asserts the theme-token switch (card/label colors), ComboBox text foreground across Dark→Light→Dark, shell-surface alpha, `ModeSelector` template/height, badges, spinner, toasts and sheets. Binding warnings are collected through a `PresentationTraceSources.DataBindingSource` listener; a clean run prints `RENDERED OK` and `TRACE_COUNT=0`. If the temp folder is cleaned, recreate it from these checks — the checks are the contract.

## Gotchas

- `release-build/` and the root `xray/` folder are untracked local artifacts (hundreds of MB). `.gitignore` only covers `bin/xray/*` binaries, so `git add -A` will stage them — don't. `sing-box/sing-box.exe` is intentionally tracked. `dist/` is a stale committed build output; leave it alone. `publish/` is gitignored (local preview builds).
- Runtime binaries resolve relative to the app directory: xray from `xray/xray.exe`, `bin/xray/xray.exe`, or `xray.exe`; sing-box from `sing-box/sing-box.exe`. Local runs need xray binaries placed manually.
- Update manifest lives at `https://elysiumhub.org/updates/elyproxy/update.json`; `UpdateService` rejects non-HTTPS, other hosts, non-`.zip` URLs, and bad SHA-256. Deploy the ZIP first and `update.json` last.
- Parts of `Core/`, `Services/`, `Models/`, `ViewModels/` are decompiler output (tabs, explicit `using`s, `this.` prefixes, the `ReadOnlyArray.cs` `<>z__ReadOnlyArray` artifact at the repo root that decompiled code depends on). Match the surrounding file's style instead of reformatting; new files use 4 spaces.
- XAML with Cyrillic must be saved as UTF-8 (no BOM is fine, CRLF like the existing files).
- `README.md` (Russian) is the user-facing doc; its project-structure section is stale and omits newer files.

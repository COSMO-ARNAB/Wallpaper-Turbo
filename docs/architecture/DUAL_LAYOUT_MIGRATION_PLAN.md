# Wallpaper Turbo — Dual-Layout Migration Plan (v2)

> **Status:** Approved, ready for execution
> **Version:** v2 (supersedes v1 in place; v1 is overwritten)
> **Architecture:** FINALIZED. Do not redesign.
> **Stack:** .NET 8 (`8.0.421`), WPF (`net8.0-windows`), Wpf.Ui 4.3.0, CommunityToolkit.Mvvm 8.4.2, `wpf-ui`, `Microsoft.Extensions.DependencyInjection` 10.0.8
> **App version at start:** 1.2.1-beta.2
> **Current branch (development line):** `feature/app-updater`
> **Working branch for this migration:** `feature/dual-layout-migration` (new, cut from `feature/app-updater`)
> **Plan commit gate:** each step ends with a `pre-stepN-rollback` tag.

---

## v2 Changelog

v1 → v2 changes, applied inline. Architecture is preserved; the corrections are targeted at lifecycle safety, layout-switching mechanics, settings partition, and execution-readiness.

**Round 2 corrections (after first-pass edits — folder paths, wrapper removal, conservatism):**
- Replaced ALL `src/WallpaperTurbo.UI/Layouts/...` path references with the v2 `src/WallpaperTurbo.UI/Views/...` subtree (`Minimal/`, `Techie/`, `Pages/`, `Widgets/`) across: Phase D File Map, Phase I Execution Roadmap (Steps 1, 2, 3, 4, 5, 6 checkboxes), H.2 per-phase rollback table, the Step 2 file tree, the Step 3 split description, the Step 4 Phase E.1 dependencies, the dependency table (1→2, 3→4 transitions), the contamination-avoidance section, and the theme-isolation grep tests.
- Removed all v1 `MainShellViewModel` wrapper references: I.4.a sub-step, ViewModels file map row, future-flow services tree (`DataContext = mainShellViewModel` → `DataContext = mainViewModel`), H.1 safety-net paragraph, Step 3 branch-split reference, and the per-finding MEDIUM-1 description. Replaced with: `MainViewModel.LayoutHost` is a sub-property resolved from DI in the constructor; `MainWindow.DataContext` remains `MainViewModel`.
- Removed v1's `TechieSettingsView.xaml(.cs)` (per Step 2.5 partition — both layouts use shared `Views/Pages/SettingsView`).
- Removed v1's `TechieModalOverlayView.xaml(.cs)` (modal stays in `MainWindow.xaml` per Step 0.5 invariant).
- Removed v1's `MainShellViewModelTests` test row; added v2 `LayoutHostReentrancyTests` and `FeatureFlagTests`.
- Added per-finding severity table at end of plan (24 findings: D-1, M-1, HIGH-1/2, MEDIUM-1–6, T-1 through T-10, MEM-01, MEM-02) with status, step, and residual risk for each.

**Architecture preserved (no changes to the design):**
- `LayoutHostView` is still the single entry point. Two layout views (`MinimalLayoutView`, `TechieLayoutView`) live as siblings.
- Minimal/Techie split unchanged; the pre-0ac1ed0 `d184105_*.xaml*` is still the Techie source-of-truth.
- `ILayoutPreferenceStore` remains narrow in `Core/Settings/`. No broader `ISettingsService`.
- Modal overlay stays in `MainWindow.xaml` (ZIndex=1000). Layout views do not host the modal.
- Per-layout theme dictionary is scoped to `LayoutView.Resources.MergedDictionaries`. `App.xaml` merges only WPF-UI `ThemesDictionary` + `ControlsDictionary` (no per-layout theme in `App.xaml` after Step 5).
- Existing VMs are singletons (pre-existing). Not refactored in this migration.
- `MainShellViewModel` wrapper is **dropped** (recommended); `MainWindow.DataContext = MainViewModel` and `MainViewModel.LayoutHost = LayoutHostViewModel` as a sub-property.
- `MainViewModel.IsDialogVisible`, `DialogTitle`, `DialogMessage` etc. stay in `MainViewModel` and bind from `MainWindow.xaml` (no duplication in layout VMs).
- `DropShadowEffect` choreography, telemetry `Storyboard` pulses, and inertia scroll in the Techie `DashboardView` are **not refactored** in this migration; only the `WM_MOUSEHWHEEL` hook refactor is on the table, gated on profiling evidence.

**Folder structure (revised intent, applied in v2):**
```
src/WallpaperTurbo.UI/Views/
  LayoutHostView.xaml(.cs)              (single entry point; DataTemplate DataType lives here)
  LayoutHostViewModel.cs
  Pages/
    DashboardView.xaml(.cs)             (shared baseline page; used by Minimal)
    LibraryView.xaml(.cs)
    SettingsView.xaml(.cs)
  Minimal/
    MinimalLayoutView.xaml(.cs)         (composes Minimal chrome + ContentPresenter)
    MinimalLayoutViewModel.cs
    Chrome/
      MinimalSidebarView.xaml(.cs)
      MinimalTitleBarView.xaml(.cs)
      MinimalUpdaterBanner.xaml(.cs)
  Techie/
    TechieLayoutView.xaml(.cs)          (composes Techie chrome + ContentPresenter)
    TechieLayoutViewModel.cs
    TechieDashboardView.xaml(.cs)       (layout-specific; uses Techie tokens; resolved via Techie DataTemplate DataType)
    Chrome/
      TechieSidebarView.xaml(.cs)
      TechieTitleBarView.xaml(.cs)
      TechieFooterView.xaml(.cs)
      TechieUpdaterBanner.xaml(.cs)
  Widgets/
    PerformanceGraph.xaml(.cs)          (shared, may be new)
    TelemetryRing.xaml(.cs)             (shared, may be new)
```

**New sub-steps added to Step Expansion:**
- **Step 0.5 — Navigation & Lifecycle Survey.** Enumerates every `MainWindow.xaml` chrome surface, produces a constraint table, and addresses lifecycle (modal overlay interaction, `MainViewModel.OnExit`, `OnClosing`). Resolves T-1 (TitleBar placement) and T-9 (SubHero binding).
- **Step 2.5 — Settings Diff Analysis.** Diff `SettingsViewModel` against the union of Minimal+Techie settings needs. Produces a settings partition table (shared, minimal-specific, techie-specific).
- **Step 3.5 — Layout Switching Mechanism Selection.** Locks in option (a) — `DataTemplate DataType` in `LayoutHostView.Resources`. Documents the XAML contract (≤10 lines). Confirms `LayoutHostViewModel` exposes `CurrentLayout` and `Layouts`, and `MainViewModel.LayoutHost` is bound from `MainWindow.xaml`.

**Blocking gate items added (must complete before Step 1 can start):**
1. **D-1 — Singleton graph coupling.** `App._serviceProvider` is a static field; service provider + all singletons are never disposed. Step 0 introduces `App.DisposeServiceProvider()` and each step's tests verify disposal.
2. **Step 0.5 — Navigation & Lifecycle Survey** (T-1 TitleBar, T-9 SubHero).
3. **T-2 — CI pipeline verification.** Confirm `<Compile>` and `<Page>` glob patterns include both `Views/Minimal/`, `Views/Techie/`, `Views/Pages/`, `Views/Widgets/`.
4. **T-3 — Feature flag scaffolding.** `WT_DISABLE_TECHIE_LAYOUT` env var + `LocalSettings` toggle. Two-tier rollback foundation.

**Missing tasks added (T-1 through T-10), slotted into Execution Roadmap:**
- T-1 TitleBar placement (resolved in Step 0.5)
- T-2 CI pipeline verification (pre-Step-1)
- T-3 Feature flag scaffolding (Step 0)
- T-4 Persist `SwitchLayout` LAST (atomic write) (Step 6)
- T-5 Theme memory test (Step 7)
- T-6 `_switchInProgress` re-entrancy guard (Step 6)
- T-7 Health metric — `LayoutSwitchDurationMs` + `LayoutSwitchErrorCount` (Step 6)
- T-8 Rollback drill (Step 6)
- T-9 SubHero1/2/3 binding decision (Step 0.5)
- T-10 Version bump decision (1.2.2 vs 1.3.0) (pre-Step-1)

**Rollback improvements (6), slotted into Rollback section:**
1. Two-tier flag: `WT_DISABLE_TECHIE_LAYOUT` outer + per-step `pre-stepN-rollback` git tag inner.
2. Atomic writes for `LocalSettings.layout` (temp file + rename pattern).
3. Persist-last ordering in `SwitchLayout` (visual first, then persist).
4. Rollback drill in Step 6 (5 minutes: flip flag, restart, verify Minimal boots, restore flag).
5. Health metric `LayoutSwitchErrorCount` and `LayoutSwitchDurationMs` alertable.
6. Rollback runbook: `docs/architecture/ROLLBACK.md` with the 5-minute drill script.

**Open questions (7 preserved from prior pass):**
- Q1: Feature flag — env var only, or also `LocalSettings` toggle surfaced in the UI?
- Q2: TitleBar fallback if it cannot move out of `MainWindow.xaml`. — **Resolved.** TitleBar stays in `MainWindow.xaml`; WPF-UI `TitleBar` is bound to parent `Window` via `WindowChrome.IsHitTestVisibleInChrome` and cannot move into a layout view. Empirical test deferred — safe assumption is "stays".
- Q3: Drop `MainShellViewModel` wrapper? — **Resolved.** `MainShellViewModel` is NOT introduced. `MainViewModel.LayoutHost` is the sub-property. (See resolved-decision row below.)
- Q4: What to do with the 5 unwired sidebar entries (Marketplace, Themes, Account, Help, About) — pre-existing in both layouts.
- Q5: `installer.iss` is 0 bytes — does this migration block on fixing it? (Confirmed out of scope; `src/WallpaperTurbo.Installer/installer.iss` is the real one and is 2,141 bytes.)
- Q6: Techie `MonitorSelector` widget — keep, defer to 1.4.0, or remove?
- Q7: Version bump — 1.3.0 (minor) or 1.2.2 (patch)?

**Resolved decisions (not open questions):**

- **Modal ownership** — Resolved. Modal overlay lives in `MainWindow.xaml` (lines 345–405 in the source). Bound to 5 `MainViewModel.Dialog*` properties + 2 `Dialog*Command`s. Layout views do not host the modal. Layout views must not duplicate the API surface.
- **Q2 (TitleBar)** — Resolved (see above). TitleBar stays in `MainWindow.xaml`.
- **Q3 (wrapper VM)** — Resolved (see above). `MainViewModel.LayoutHost` is the integration point; no wrapper.

**Estimated effort delta: +5 days on 70–90h base** (new sub-steps 0.5/2.5/3.5 add ~12h; T-1 through T-10 add ~16h; rollback improvements add ~6h; 4 blocking gate items add ~6h).

**Per-finding severity table (re-derived, full table at end of plan):**
- D-1 (CRITICAL): singleton graph coupling → Step 0 pre-step gate.
- M-1 (CRITICAL): lifecycle hooks missing → Step 0.5.
- HIGH-1: SubHero1/2/3 — properties exist in `DashboardViewModel`, but no XAML element has `Tag="SubHero1/2/3"`. Reframed as a decision, not a compile blocker.
- HIGH-2: T-1 TitleBar placement (WPF-UI TitleBar may require Window placement) → Step 0.5.
- MEDIUM-1: wrapper (previously `MainShellViewModel`) — dropped, `MainViewModel.LayoutHost` is the sub-property → Step 3.
- MEDIUM-2: Per-layout theme dictionary scoping → Step 5.
- MEDIUM-3: Modal overlay in `MainWindow.xaml` — must not move to layout view → Step 3.
- MEDIUM-4: `DataTemplate DataType` XAML contract in `LayoutHostView.Resources` → Step 3.5.
- MEDIUM-5: `SwitchLayout` semantics (apply first, persist last, idempotent, re-entrant guard) → Step 6.
- MEDIUM-6: Step 5 conservatism (no refactor of `DropShadowEffect`, telemetry pulses, inertia scroll) → Step 5.

---

## v3 Changelog (this is the execution plan)

Final cleanup pass. v3 is canonical — no further planning rounds. Five surgical edits applied:

1. **MainShellViewModel references:** all remaining instances removed.
2. **Disposal model:** `App.DisposeServiceProvider()` → `App.OnExit → DisposeServices()` calling targeted `IDisposable` on `MainViewModel`, `LayoutHostViewModel`, and future services.
3. **TitleBar + Modal decisions:** marked Resolved. TitleBar stays in `MainWindow.xaml`; modal overlay stays in `MainWindow.xaml`.
4. **.sln investigation:** removed from critical path. Not required. Create one only if `dotnet test` or other tooling fails because of its absence.
5. **Pre-Step-1 gates:** collapsed from 4 to 2 — BG-1 (Step 0 housekeeping) and BG-2 (T-2 CI verification). T-3 (feature flag) is no longer a pre-Step-1 gate.

Architecture source-of-truth diagram added (MainWindow → LayoutHostView → Layouts; MainViewModel.LayoutHost).

**This document is now the execution plan.**

---

## v3 Architecture Review Update

Six surgical updates from a fresh architecture review. No redesign. No implementation code. Roadmap only.

1. **BG-1 scope:** `LayoutHostViewModel` placeholder/skeleton **removed** from housekeeping. BG-1 is now: branch creation, AGENTS.md refresh, CI verification support, D-1 leak fix planning. `LayoutHostViewModel` belongs to Step 3A, not housekeeping.

2. **D-1 Closure split into two parts:**
   - **Part A (approved for implementation):** `MainViewModel` implements `IDisposable`; unsubscribe from `_telemetryService.MetricsUpdated` in `Dispose()`. Lands in Step 1 Scaffolding.
   - **Part B (planning only):** `TelemetryService` `IDisposable` review — timer ownership, timer disposal strategy, shutdown path, `App.OnExit` interaction. Lifecycle ownership must be documented before implementation.

3. **App Disposal Strategy:** replaced `App.OnExit → DisposeServices()` abstraction with an explicit enumerated list:
   ```text
   App.OnExit
     └─ Dispose known IDisposable components explicitly
        ├─ UpdateCoordinator (already disposed)
        ├─ MainViewModel (D-1 Part A, approved)
        ├─ TelemetryService (D-1 Part B, only if review approves)
        └─ Future LayoutHostViewModel (Step 3A, documented first)
   ```
   Do **NOT** dispose `IServiceProvider`.

4. **LayoutHostViewModel ownership:** navigation ownership stays in `MainViewModel`. `LayoutHostViewModel` is a chrome/container concern only (owns: `CurrentLayout`, `Layouts`, `SwitchLayout()`, `ILayoutPreferenceStore`). `MainViewModel.NavigateCommand` continues to handle page navigation unchanged.

5. **Step 1 Scaffolding:** Step 1 is scoped DOWN to scaffolding only (folder structure, placeholder files, build verification). Do NOT create `LayoutHostViewModel` in Step 1. Do NOT begin Minimal/Techie extraction in Step 1.

6. **Proceed in this order:** BG-2 CI verification → D-1 leak fix → Branch preparation → Step 1 Scaffolding.

**Tracking — new task added:**

- **T-12 (NEW, planning only):** `TelemetryService` `IDisposable` review — D-1 Part B. Document timer ownership, timer disposal strategy, shutdown path, `App.OnExit` interaction BEFORE implementing. Lands in Step 1 Scaffolding as a planning artifact (`docs/architecture/TELEMETRY_SERVICE_LIFECYCLE.md`); implementation deferred until documentation is reviewed.

**Step 1 Scaffolding — additional task (D-1 Part A implementation):**

- D-1 Part A: `MainViewModel` implements `IDisposable`; `Dispose()` unsubscribes from `_telemetryService.MetricsUpdated`. Lands in Step 1 Scaffolding as a small additive code change (does not conflict with the "scaffolding only" constraint — it is a leak fix on an existing VM, not Layout/Minimal/Techie extraction).

---

## Source-of-truth verification (done before this plan was written)

1. `0ac1ed0` IS the "Cinematic Console" redesign = the current **Minimal** layout (240px sidebar, 7-metric footer removed, 265-line `DashboardView`, single-accent theme).
2. `pre-0ac1ed0` IS the **Techie** layout (250px sidebar, "Now Playing" widget, 5-button Quick Access, 7-metric cyber footer, "Current Experience" strobe, dual cyan/purple glow theme, inertia scroll).
3. `d184105_DashboardView.xaml/.xaml.cs` in repo root are **untracked** files (`git status` shows `A `) on branch `feature/app-updater`; they are a verbatim copy of pre-`0ac1ed0` `DashboardView` (250 lines `.cs` with inertia scroll intact, 861 lines `.xaml` with neon glow). This is the **restoration source**.
4. `LayoutHostView`, `MinimalLayoutView`, `TechieLayoutView` **do not yet exist** anywhere — they are net-new in this migration.
5. Current `MainWindow.xaml` (407 lines) owns the sidebar, header, updater banner, content presenter, and modal overlay. Sidebar + header **must move out** of `MainWindow` for the `LayoutHostView` split to be meaningful. **The modal overlay (Grid ColumnSpan=2 ZIndex=1000, lines 345–405) STAYS in `MainWindow.xaml` per the Step 0.5 invariant.**
6. Current theme `Theme/NeonTechStyle.xaml` (348 lines) uses the Minimal token system (`Surface0/1/2/3`, `TextPrimary/Secondary/Tertiary`, `Accent`, `StatusOk/Warn/Err`, `Card`, `CardHover`, `NavItem`, etc.). The pre-`0ac1ed0` file uses an entirely different token set (`ActiveCyan/Purple/Green`, `WarningRed`, `NeonCardStyle`, `SidebarButtonStyle`, `NeonImportButtonStyle`, `QuickAccessCardButtonStyle`, `PremiumCardContainerStyle`, `PremiumCardBgGradient`, `NeonPurpleCyanGradient`, `TelemetryLabelStyle`, `TelemetryMetricStyle`).
7. Settings persistence flows through `IUpdaterSettingsStore` → `JsonUpdaterSettingsStore` for the updater, and `WallpaperService` for the engine. There is **no layout-persistence service yet** — this plan adds one in Step 6.
8. Tests live in `tests/WallpaperTurbo.Tests/` and are updater/version-focused (`GitHubReleaseProvider*`, `SemanticVersionTests`, `UpdateCoordinatorVerificationTests`). No UI tests, no nav tests, no layout tests yet.
9. The `git grep` baseline is 27/27 tests passing on the `0ac1ed0` commit. Step 7's goal is to keep that 27/27 and add ≥10 new tests for the layout split.
10. **D-1 (NEW, v3 Architecture Review Update):** `App.xaml.cs` line 19 declares `private static readonly IServiceProvider _serviceProvider = ConfigureServices();`. `OnExit` (line 136) disposes only the `UpdateCoordinator`. The service provider and all other singletons are never disposed. This is a **pre-existing leak** that becomes more important after the migration because layout switching will hold more singletons (`LayoutHostViewModel`, layout VMs, theme dictionaries). **D-1 is split into two parts:** Part A (`MainViewModel` `IDisposable` + telemetry unsubscribe) is approved for implementation in Step 1 Scaffolding. Part B (`TelemetryService` `IDisposable` review) is planning only (T-12). **`App.OnExit` uses an explicit enumerated list of known `IDisposable` components** in registration/declaration order — `UpdateCoordinator` (already disposed), `MainViewModel` (D-1 Part A, approved), `TelemetryService` (D-1 Part B, only if review approves), future `LayoutHostViewModel` (Step 3A, documented first). **Do NOT dispose `IServiceProvider`** — that would tear down WPF-UI singletons. The `try/catch` disposal of `UpdateCoordinator` in `OnExit` is the model pattern; new components follow the same shape.
11. **T-9 (NEW):** `d184105_DashboardView.xaml.cs` lines 240–242 reference `vm.SubHero1/2/3` via `DataContext is not DashboardViewModel vm`. These properties **do exist** in `DashboardViewModel` (lines 68–70), but no XAML element in either the Techie or Minimal `DashboardView.xaml` has `Tag="SubHero1/2/3"`. The references are dead code that compiles cleanly. T-9 is reframed as a **decision** (add SubHero XAML elements to Techie, or accept the dead references) — not a compile blocker.
12. **M-1 (NEW, v3 Architecture Review Update):** `MainWindow.xaml.cs` line 173 awaits `viewModel.ShutdownAsync()`. The plan must address: (a) when `SwitchLayout` is called, the modal overlay must remain functional, (b) the layout VMs must not be holding handlers to `MainViewModel` events that would prevent GC after `OnClosing`, (c) `App.OnExit` must dispose the new `LayoutHostViewModel` (introduced in Step 3A) so it can release `MainViewModel` references — the lifecycle ownership of `LayoutHostViewModel` MUST be documented in `docs/architecture/LAYOUT_HOST_LIFECYCLE.md` BEFORE its implementation in Step 3A. Addressed in Step 0.5.
13. **T-2 (NEW):** `.github/workflows/release.yml` line 25 does `dotnet restore src/WallpaperTurbo.UI/WallpaperTurbo.UI.csproj -p:Platform=x64`. Since WPF projects use SDK-style globbing (`<UseWPF>true</UseWPF>` auto-picks `.xaml`/`.xaml.cs` under the project), no `.sln` change is required. **Verify by running `dotnet build src/WallpaperTurbo.UI/WallpaperTurbo.UI.csproj -c Release -p:Platform=x64` after Step 1 lands.**
14. **`.sln` file:** not required. The project is built via `dotnet build` against the `.csproj` directly. The v1 plan recommended adding one; **v3 does not** — out of scope, the build works without it. If `dotnet test` or other tooling fails because of its absence, create one as a fix-forward action.
15. **`installer.iss` root (preserved open question):** Root `installer.iss` is 0 bytes. **Confirmed out of scope** — `src/WallpaperTurbo.Installer/installer.iss` is the real one (2,141 bytes) and is what `.github/workflows/release.yml` line 37 uses.
16. **Root AGENTS.md (preserved open question):** Stale (describes a "skills" repo). Out of scope for this migration; flag for a future housekeeping PR.

These sixteen facts anchor the plan. The plan does not re-litigate any of them.

---

# Phase A — Repository Analysis

## A.0 Architecture source-of-truth (canonical, v3)

```text
MainWindow
 ├─ TitleBar
 ├─ Modal Overlay
 └─ LayoutHostView

MainViewModel
 └─ LayoutHost (LayoutHostViewModel)

LayoutHostViewModel
 ├─ CurrentLayout
 ├─ Layouts
 ├─ SwitchLayout()
 └─ ILayoutPreferenceStore
```

> **LayoutHostViewModel ownership (v3 Architecture Review Update):** Navigation ownership stays in `MainViewModel`. `LayoutHostViewModel` does **NOT** become the navigation owner. `LayoutHostViewModel` is a **chrome/container concern** only — it owns `CurrentLayout`, `Layouts`, `SwitchLayout()`, and `ILayoutPreferenceStore`. `MainViewModel` retains `CurrentPageViewModel`, the `NavigateCommand` (3-destination dashboard/library/settings navigation), and the child page VMs (`DashboardViewModel`, `LibraryViewModel`, `SettingsViewModel`). The pre-existing `MainViewModel.NavigateCommand` continues to handle page navigation unchanged. `LayoutHostViewModel` is a layout-selection host, not a navigation host.

## A.1 Required file review order

Read **before writing any code**, in this exact order. Each step depends on the previous one and produces a verification artifact.

| # | Path | Reason | Artifact to produce |
|---|------|--------|---------------------|
| 1 | `Directory.Build.props`, `global.json`, root `package.json` | Confirm SDK (`.NET 8.0.421`), versioning (1.2.1-beta.2), and that no JS surface affects the migration | "Toolchain Snapshot" note |
| 2 | `src/WallpaperTurbo.UI/WallpaperTurbo.UI.csproj`, `App.xaml`, `App.xaml.cs` | Confirm the WPF app startup, DI graph entry point, and resource-dictionary merge order (`NeonTechStyle` is last → it wins) | "DI Manifest" — list of every singleton |
| 3 | `src/WallpaperTurbo.UI/Theme/NeonTechStyle.xaml` | Lock down the current Minimal token set; this becomes the "do not regress" surface | "Minimal token inventory" |
| 4 | `src/WallpaperTurbo.UI/MainWindow.xaml` + `.cs` | Lock down the chrome the layouts must reproduce (`WindowChrome`, DWM Mica, Icon cropping, `DwmSetWindowAttribute` calls, Deactivated/Minimized safety guards, `OnClosing` shutdown dance) | "MainWindow contract" — list of services + behaviours |
| 5 | `src/WallpaperTurbo.UI/ViewModels/MainViewModel.cs` | Lock down the navigation command surface (`Dashboard` / `Library` / `Settings`), `CurrentPageViewModel`, telemetry fan-out, engine toggle, import pipeline, dialog flow, shutdown | "MainViewModel surface" |
| 6 | `src/WallpaperTurbo.UI/Views/DashboardView.xaml` + `.cs`, `LibraryView.xaml` + `.cs`, `SettingsView.xaml` + `.cs` | Each is a `UserControl` bound to its VM by `DataTemplate DataType` in `MainWindow` — they must keep the same contract | "UserControl contract" — XAML root type, bindings, code-behind events |
| 7 | `src/WallpaperTurbo.UI/ViewModels/DashboardViewModel.cs`, `LibraryViewModel.cs`, `SettingsViewModel.cs`, `UpdaterViewModel.cs` | The single source of truth for what data is available to layouts. Anything layouts need that is not here is a bug | "VM capability matrix" |
| 8 | `src/WallpaperTurbo.UI/Services/*.cs` | `WallpaperService`, `TelemetryService`, `IWallpaperLibraryService`, `IWallpaperPreviewService`, `IThumbnailExtractor`, `DiagnosticsService`, `JsonUpdaterSettingsStore`, `FallbackTelemetryProvider`, `PerformanceCounterTelemetryProvider` | "Service contract" |
| 9 | `src/WallpaperTurbo.UI/Controls/*.xaml(.cs)`, `Converters/*.cs` | `PerformanceGraph`, `TelemetryRing`, `VirtualizingWrapPanel`, `ThumbnailImageConverter` — shared controls; layouts must not break them | "Shared control inventory" |
| 10 | `d184105_DashboardView.xaml` + `.xaml.cs` in repo root | The Techie source. Note: references `vm.SubHero1/2/3`, `vm.IsEngineRunning` via `AncestorType=Window`, `WallpaperEntry` — must verify against current VMs | "Techie → current VM gap" |
| 11 | `git show 0ac1ed0^:src/WallpaperTurbo.UI/MainWindow.xaml` and `:Theme/NeonTechStyle.xaml`, `:Views/DashboardView.xaml`, `:Views/LibraryView.xaml`, `:Views/SettingsView.xaml`, `:Views/DashboardView.xaml.cs` | Full pre-`0ac1ed0` source for all five UI surfaces (the Techie universe) | "Techie baseline files" snapshot |
| 12 | `src/WallpaperTurbo.Core/**` | `MonitorManager`, `MonitorSession`, `WallpaperSession*`, `MediaPipeline`, `DesktopComposition*`, `WindowUtil`, `NativeMethods` — backend, shared by both layouts (must remain untouched) | "Backend touch-list = empty" confirmation |
| 13 | `src/WallpaperTurbo.Updater/**` | Same — backend, untouched | "Updater touch-list = empty" |
| 14 | `tests/WallpaperTurbo.Tests/**` | Establishes baseline (27/27 passing per `0ac1ed0` commit msg). New tests will be added, not modified | "Test baseline" — currently zero UI tests |
| 15 | `docs/libvlc-architecture-report.md`, `docs/libmpv-architecture-report.md`, `docs/renderer-architecture-spec.md` | Read only the **navigation / VM / settings** sections if any. Otherwise, skip | "Docs are renderer-only, not relevant to layout split" |
| 16 | `.github/workflows/release.yml` | Confirm `dotnet restore` targets `WallpaperTurbo.UI.csproj` and `WallpaperTurbo.AppRunner.csproj`. **T-2: confirm WPF SDK globbing picks up new `Views/Minimal/`, `Views/Techie/`, `Views/Pages/`, `Views/Widgets/` paths.** | "CI glob verification" |
| 17 | `installer.iss` (root) and `src/WallpaperTurbo.Installer/installer.iss` | Confirmed: root is 0 bytes (out of scope), `src/.../installer.iss` is the real one (2,141 bytes) and is what CI uses | "installer.iss: not blocking" |

## A.2 Dependency graph (what depends on what)

```
(no .sln file currently — project is built via `dotnet build` against the .csproj directly; create one only if tooling requires it)
└── WallpaperTurbo.UI (WinExe, net8.0-windows, UseWPF)
    ├── WallpaperTurbo.Core       (referenced) ← backend
    ├── WallpaperTurbo.Updater    (referenced) ← backend
    ├── CommunityToolkit.Mvvm     (8.4.2)
    ├── Microsoft.Extensions.DependencyInjection (10.0.8)
    ├── System.Diagnostics.PerformanceCounter
    └── wpf-ui (4.3.0)

Runtime DI graph (verified from App.xaml.cs):
  - Services:   IThumbnailExtractor → WpfThumbnailExtractor
                IWallpaperLibraryService → WallpaperLibraryService
                WallpaperService (concrete)
                TelemetryService (concrete)
                IWallpaperPreviewService → WallpaperPreviewService
                DiagnosticsService (concrete)
                + Updater stack (8 services + UpdateCoordinator)
  - ViewModels: MainViewModel, DashboardViewModel, LibraryViewModel,
                SettingsViewModel, UpdaterViewModel
  - Windows:    MainWindow

MainViewModel constructor dependencies:
  WallpaperService, TelemetryService, IWallpaperLibraryService,
  UpdaterViewModel, DashboardViewModel, LibraryViewModel, SettingsViewModel

MainWindow constructor dependencies:
  MainViewModel, IWallpaperPreviewService, DiagnosticsService
```

**Architectural invariants to verify before any change**

1. `App.xaml` merge order — `NeonTechStyle` is last and wins. Adding a second theme dictionary that loses to it would silently break Techie. This must be made **explicit and dynamic** by the migration.
2. `MainWindow.xaml.Resources` contains the `DataTemplate DataType="{x:Type vm:DashboardViewModel}" → <views:DashboardView />` mapping. If layouts swap UserControls but keep the same VMs, the DataTemplate must move with them.
3. `MainViewModel._currentPageViewModel` is set in the constructor (no setter for default page; `_currentPageViewModel = _dashboardViewModel`). `NavigateCommand` only handles three destinations — `Playlists`, `Monitor setup`, `Engine`, `Performance`, `About` are **not wired**. Migration must NOT rely on them being wired.
4. The `AncestorType=Window` binding trick is used in **both** Techie and Minimal DashboardViews to reach `IsEngineRunning`, `ImportWallpaperCommand`, `NavigateCommand` on the `MainViewModel`. The migration must preserve this pattern in both layouts.
5. `WindowChrome.IsHitTestVisibleInChrome` is used pervasively in the custom titlebar of both designs. Titlebar / chrome / DWM calls are owned by `MainWindow` today and must remain owned by it (the `LayoutHostView` sits inside `MainWindow`'s content area, not the chrome).

## A.3 Risk areas

| Risk | Likelihood | Impact | Why it matters here |
|------|-----------|--------|---------------------|
| Theme resource collisions — both layouts try to define `AccentBrush` with different values | High | High | Both pre-`0ac1ed0` and current `NeonTechStyle` define brush resources with conflicting meanings. Loading both naïvely will cause the wrong brush to be resolved depending on merge order. |
| DataTemplate collisions — both `MinimalLayoutView` and `TechieLayoutView` would map `DashboardViewModel → SomeUserControl` | High | High | Window-level DataTemplates in `MainWindow.Resources` cannot easily be layout-scoped. Must move to LayoutHost-level `DataTemplate DataType` resolution. |
| `MainWindow` has logic in code-behind (Icon crop, DWM, preview cancel, OnClosing) | Certain | Medium | Any "thin MainWindow" approach must keep this code. `MainWindow.xaml.cs` is **185 lines** with non-trivial platform code. |
| `AncestorType=Window` resolution changes when the visual tree is reorganized | Medium | High | If Techie's sidebar/header move into a sub-UserControl, `RelativeSource AncestorType=Window` still works (it walks the visual tree to the Window), and the DataContext chain at the Window level is `MainViewModel`. Layouts that wrap the content in their own UserControl with their own DataContext must keep the Window as the binding source. |
| Memory — `CompositionTarget.Rendering` leak from pre-`0ac1ed0` DashboardView | Certain (if Techie is restored verbatim) | Medium | The d184105 `DashboardView.xaml.cs` hooks `CompositionTarget.Rendering` and only unhooks on `Unloaded`. The code is correct **as long as** the UserControl is removed from the visual tree on layout switch. The migration must verify this happens. |
| Settings layout not persisted | Certain | Medium | There is no layout preference store today. Users would lose their layout choice on every restart unless one is added. |
| `OnCardMouseLeftButtonDown` references `SubHero1/2/3` in Techie but Minimal DashboardViewModel doesn't expose them | Certain | Low | Techie expects 3 sub-hero cards; current `DashboardViewModel` exposes only `CurrentWallpaper`. Decision taken in Step 5: add 3 read-only view-state properties to `DashboardViewModel` mirroring `RecentlyUsedWallpapers[0..2]`. |
| Inertia scroll input handler (`WM_MOUSEHWHEEL`) | Certain (if Techie is restored verbatim) | Low | Works fine if the UserControl is loaded/unloaded properly. Not a blocker. |
| `App.xaml` resource dictionary not dynamic | Certain | High | Today the theme is a static `ResourceDictionary Source="Theme/NeonTechStyle.xaml"`. To swap themes by layout, this must become conditional — either by moving to a `MergedDictionaries` swap at runtime, or by giving each layout its own scoped resource dictionary. |
| Modal dialog (`IsDialogVisible`, `DialogTitle`, etc.) lives in `MainWindow.xaml` `Grid ColumnSpan=2` | Certain | Low | Modal must remain on top of the active layout. LayoutHost must not block the modal overlay's `ZIndex=1000`. |
| Test coverage gap | Certain | Medium | Today there are no tests for navigation, layout switching, or VM↔View binding. The migration introduces new logic; without tests it is un-mergeable. |
| **D-1 (NEW, v3 Architecture Review Update):** Singleton graph coupling — `App._serviceProvider` is a static field, never disposed | Certain | High | Layout switching will hold more singletons (`LayoutHostViewModel`, `LayoutView` instances, theme dictionaries). If a switch throws or the app exits without disposing, references from the static provider to the singletons persist. **v3 resolution (split into two parts):** Part A — `MainViewModel` implements `IDisposable`; `Dispose()` unsubscribes from `_telemetryService.MetricsUpdated`. Approved for implementation, lands in Step 1 Scaffolding. Part B — `TelemetryService` `IDisposable` review is planning only (T-12); lifecycle ownership documented in `docs/architecture/TELEMETRY_SERVICE_LIFECYCLE.md` before implementation. **`App.OnExit` uses an explicit enumerated list of known `IDisposable` components** (NOT `DisposeServices()`, NOT `IServiceProvider.Dispose()`). The `try/catch` disposal of `UpdateCoordinator` in `OnExit` is the model pattern. `LayoutHostViewModel` is NOT introduced in BG-1 — it belongs to Step 3A. Listed in the Blocking Gate Items list. |
| **M-1 (NEW):** Lifecycle hooks missing — layout switching interacts with modal overlay, `OnExit`, `OnClosing` | Certain | Medium | `MainWindow.xaml.cs` line 173 awaits `viewModel.ShutdownAsync()`. If the user clicks "Switch layout" while the modal is visible, the modal must remain visible across the switch. If the user closes the window mid-switch, the in-progress switch must not block shutdown. **v2 resolution:** Step 0.5 enumerates the lifecycle interactions and produces a constraint table. |

---

# Phase B — Detailed Step Expansion

The existing plan (Steps 0–7) is preserved. Each step adds the eight required dimensions. Complexity is rated `S` (< 4h), `M` (4–16h), `L` (16–40h), `XL` (> 40h).

## Blocking Gate Items (must complete before Step 1 can start)

**v3 simplification:** the four pre-Step-1 gates from v2 are collapsed to two. Step 0.5 is folded into Step 0 housekeeping (the ownership decisions are made; the survey is unnecessary as a separate step). T-3 (feature flag) is no longer a pre-Step-1 gate — it lands in Step 1 or Step 2. T-2 (CI verification) is the last gate before Step 1 starts. Both gates must be green before Step 1 begins.

| # | Gate item | Owner | Effort | Verification |
|---|-----------|-------|--------|--------------|
| BG-1 | **Step 0 housekeeping.** Cut branch `feature/dual-layout-migration` from `feature/app-updater`. Refresh root `AGENTS.md` (currently stale, claims to be a "skills" repo). Run T-9 SubHero binding check (properties exist on `DashboardViewModel`; deferred to 1.4.0 ticket). Plan the D-1 leak fix (per the v3 Architecture Review Update): Part A `MainViewModel` `IDisposable` + telemetry unsubscribe is approved for implementation in Step 1 Scaffolding; Part B `TelemetryService` `IDisposable` review (T-12) is planning only — lifecycle ownership MUST be documented before implementation. `LayoutHostViewModel` is NOT introduced in BG-1; it belongs to Step 3A. | Step 0 | S (2–4h) | Branch created. `AGENTS.md` rewritten. T-9 documented as deferred. T-12 (`docs/architecture/TELEMETRY_SERVICE_LIFECYCLE.md`) drafted. No code changes to `App.xaml.cs` for disposal in BG-1 — the explicit enumeration in `App.OnExit` is a Step 1 Scaffolding change. Manual: `git log` shows the housekeeping commit is docs + branch only. |
| BG-2 | **T-2 — CI pipeline verification (last gate).** Run `dotnet build src/WallpaperTurbo.UI/WallpaperTurbo.UI.csproj -c Release -p:Platform=x64` against the v1 source to establish the baseline. After Step 1 lands (which adds new `.xaml/.cs` files under `Views/Minimal/`, `Views/Techie/`, `Views/Pages/`, `Views/Widgets/`), re-run the build. Verify WPF SDK globbing picks up the new files. Confirm `<Compile>` and `<Page>` globs in `Build.Actions` cover `Views/Minimal/` and `Views/Techie/` — no explicit items needed. | Step 0 | S (1h) | Build succeeds with 0 warnings, 0 errors. `.csproj` is unchanged from v1. |

Both BG-1 and BG-2 must be green before Step 1 can start.

---

## Step 0 — Navigation Architecture Snapshot + D-1 Disposal Scaffolding

**Objective** Produce a single source of truth (a markdown doc under `docs/architecture/`) that maps every navigation entry to (VM, View, DataTemplate, accessibility name, deep-link, and any code-behind handlers). Becomes the contract for all subsequent steps. **In v2, also address D-1 (singleton disposal) and T-3 (feature flag scaffolding).**

**Files affected**
- New: `docs/architecture/NAVIGATION_SNAPSHOT.md`
- New: `docs/architecture/VM_CAPABILITY_MATRIX.md`
- New: `docs/architecture/SHARED_BACKEND_INVARIANTS.md`
- New: `docs/architecture/TELEMETRY_SERVICE_LIFECYCLE.md` (T-12 — D-1 Part B planning artifact: timer ownership, timer disposal strategy, shutdown path, `App.OnExit` interaction).
- New: `src/WallpaperTurbo.UI/Services/FeatureFlagService.cs` (T-3 — env var + LocalSettings read. T-3 is no longer a pre-Step-1 gate; lands in Step 1 or Step 2 as a regular task, not a blocking gate.)
- New: `tests/WallpaperTurbo.Tests/Layout/FeatureFlagTests.cs` (T-3 test).
- No `App.xaml.cs` source-code changes in Step 0 housekeeping. Disposal changes (the explicit enumeration in `App.OnExit`) are Step 1 Scaffolding changes per the v3 Architecture Review Update.
- No other source code touched.

**Expected modifications**
- Document the existing 8 sidebar entries: `Dashboard`, `Library`, `Playlists`, `Monitor setup`, `Engine`, `Performance`, `Settings`, `About`. Of these, only 3 (`Dashboard`, `Library`, `Settings`) are wired through `MainViewModel.NavigateCommand`. The rest render but do nothing on click.
- For each VM, list observable properties, commands, and which of these the current `DashboardView` / `LibraryView` / `SettingsView` actually bind.
- Document `MainViewModel.CurrentPageViewModel` as the only navigation surface today. There is no `INavigationService` interface, no `ICommand Navigate` with `Uri`-style deep links, no back-stack, no journal.
- Document that `AncestorType=Window` is the cross-cutting binding source for engine/telemetry/dialog state.
- **D-1 (NEW, v3 Architecture Review Update):** This step does NOT add disposal code. D-1 is now split into two parts. Part A (`MainViewModel` `IDisposable` + `_telemetryService.MetricsUpdated` unsubscribe) is approved for implementation and lands in Step 1 Scaffolding as a small additive change. Part B (`TelemetryService` `IDisposable` review) is planning only — produce `docs/architecture/TELEMETRY_SERVICE_LIFECYCLE.md` documenting timer ownership, timer disposal strategy, shutdown path, and `App.OnExit` interaction. Lifecycle ownership MUST be documented before any implementation.
- **T-3 (NEW):** Add `FeatureFlagService` with `bool IsTechieLayoutDisabled` (env var `WT_DISABLE_TECHIE_LAYOUT` + `LocalSettings.techieDisabled` toggle). Wire into `App.OnStartup` to log the value at startup. **No consumer yet** — Step 4 consults it.

**Risks**
- Drift: the doc can become out-of-date in one PR. Mitigation — check it into CI as a "frozen contract" until Step 6 finishes; after Step 6, regenerate it as a build artifact or via a `dotnet-codegen` step.
- Over-documentation: trying to capture every binding is wasted effort. Cap at the entries the layouts actually need.
- D-1 disposal: changing `OnExit` could mask a real bug if disposal reveals an order-of-destruction issue. Mitigation: run the 27/27 tests + a manual exit/launch cycle. If a test fails, the failure pinpoints the singleton that was relying on the leak.
- T-3 feature flag: surfacing the toggle in the UI is a UX decision (Q1 in Open Questions). Step 0 may add the toggle but mark it "(advanced)" so it doesn't dominate the Settings view.

**Validation criteria**
- The doc is reviewable in < 15 minutes by a senior engineer unfamiliar with the repo.
- Every VM observable property referenced in XAML appears in the capability matrix.
- `MainViewModel.NavigateCommand` switch arms are exhaustively listed with their target VMs.
- **D-1 (BG-1, v3 Architecture Review Update):** `MainViewModel_Dispose_UnhooksTelemetry` test passes (lands in Step 1 Scaffolding, not BG-1). BG-1 produces the planning artifacts: `docs/architecture/TELEMETRY_SERVICE_LIFECYCLE.md` (T-12, D-1 Part B) and the D-1 Part A scope document. `LayoutHostViewModel_Dispose_UnhooksAndReleasesStore` test is deferred to Step 3A — `LayoutHostViewModel` does not exist in BG-1.
- **T-3 (lands in Step 1 or Step 2; not a pre-Step-1 gate):** `FeatureFlag_IsTechieLayoutDisabled_ReadsFromEnvVar` test passes; `FeatureFlag_IsTechieLayoutDisabled_ReadsFromLocalSettings` test passes.
- **T-2 (BG-2 — the last gate before Step 1 starts):** `dotnet build src/WallpaperTurbo.UI/WallpaperTurbo.UI.csproj -c Release -p:Platform=x64` succeeds.

**Rollback strategy** Doc-only + additive (D-1, T-3). Rollback is `git rm` the docs and `git revert` the `App.xaml.cs` and `FeatureFlagService.cs` changes. Low risk.

**Estimated complexity** S+M (4–6h)

**Prerequisites** None. **But: do not start Step 1 until BG-1 (Step 0 housekeeping) and BG-2 (T-2 CI verification) are green.**

---

## Step 0.5 — Navigation & Lifecycle Survey (NEW in v2; collapsed into Step 0 in v3)

**v3 note:** Step 0.5 is collapsed into Step 0 housekeeping (BG-1) — the survey is reference material for Step 0, not a separate blocking step. The chrome surface table, constraint table, and lifecycle interaction table below inform Step 0's authoring of `NAVIGATION_SNAPSHOT.md`, `VM_CAPABILITY_MATRIX.md`, and `SHARED_BACKEND_INVARIANTS.md`. T-1 (TitleBar) and T-9 (SubHero) are both **Resolved** as of v3 (see Resolved decisions above).

**Objective** Enumerate every `MainWindow.xaml` chrome surface (sidebar, header, updater banner, content presenter, modal overlay) and what it binds. Produce a constraint table layout views must not violate. Address T-1 (TitleBar placement) and T-9 (SubHero binding). Address M-1 (lifecycle interactions between layout switching and modal/OnExit/OnClosing).

**Files affected**
- New: `docs/architecture/NAVIGATION_AND_LIFECYCLE_SURVEY.md`
- New: `docs/architecture/T1_TITLEBAR_DECISION.md` (T-1)
- New: `docs/architecture/T9_SUBHERO_DECISION.md` (T-9)
- No source code touched.

**Expected modifications**

- **Chrome surface table** (one row per chrome element in `MainWindow.xaml`):

| Chrome surface | Lines (XAML) | What it binds | Must move out for MinimalLayoutView? | Must move out for TechieLayoutView? |
|----------------|--------------|---------------|---------------------------------------|-------------------------------------|
| Sidebar (Border Grid.Column=0) | 53–201 | `MainViewModel.NavigateCommand`, `MainViewModel.IsEngineRunning`, `MainViewModel.ActiveRendererText`, `MainViewModel.FpsText`, `MainViewModel.VramText`, `MainViewModel.ToggleEngineCommand` | YES → `Views/Minimal/Chrome/MinimalSidebarView.xaml` | YES → `Views/Techie/Chrome/TechieSidebarView.xaml` |
| Header (Border Grid.Row=0) | 210–253 | `MainViewModel.ImportWallpaperCommand` (Import button) | YES → `Views/Minimal/Chrome/MinimalTitleBarView.xaml` | YES → `Views/Techie/Chrome/TechieTitleBarView.xaml` |
| WPF-UI `<ui:TitleBar>` (Grid Grid.Row=0) | 211 | None (it's a chrome host) | **T-1 decision required** (see below) | **T-1 decision required** |
| Updater banner (Border Grid.Row=1) | 255–338 | `MainViewModel.Updater.*` (AvailableVersionText, StatusText, IsBusy, ProgressPercent, ProgressText, DownloadSpeedText, IsDownloadButtonVisible, IsInstallButtonVisible, IsCancelButtonVisible, IsNotificationVisible, DownloadCommand, RequestInstallUpdateCommand, CancelCommand, DismissNotificationCommand) | YES → `Views/Minimal/Chrome/MinimalUpdaterBanner.xaml` | YES → `Views/Techie/Chrome/TechieUpdaterBanner.xaml` |
| Content presenter (ContentPresenter Grid.Row=2) | 340–342 | `MainViewModel.CurrentPageViewModel` | YES (becomes `MinimalLayoutView`'s internal ContentPresenter) | YES (becomes `TechieLayoutView`'s internal ContentPresenter) |
| Modal overlay (Grid ColumnSpan=2 ZIndex=1000) | 345–405 | `MainViewModel.IsDialogVisible`, `DialogTitle`, `DialogMessage`, `IsDialogCancelVisible`, `DialogConfirmCommand`, `DialogCancelCommand` | **NO — stays in MainWindow.xaml** | **NO — stays in MainWindow.xaml** |

- **Constraint table (what layout views MUST NOT do):**

| Constraint | Rationale |
|------------|-----------|
| Layout views must not declare `IsDialogVisible`, `DialogTitle`, `DialogMessage`, `IsDialogCancelVisible`, `DialogConfirmCommand`, `DialogCancelCommand` properties. | The modal state lives in `MainViewModel`. Duplicating in layout VMs would cause the modal to "disappear" on layout switch. |
| Layout views must not host their own modal overlay. | The modal must overlay both layouts uniformly. `Panel.ZIndex=1000` in `MainWindow.xaml` is the only correct placement. |
| Layout views must not subscribe to `MainViewModel` events directly (e.g., `_mainVm.PropertyChanged += …`). | Layouts bind. If a layout needs to react to a `MainViewModel` state change, expose a sub-property on the layout VM that the layout binds. The subscription lives in the layout VM's constructor and is unsubscribed in its `Dispose` (added in Step 3). |
| Layout views must not reach across to each other (e.g., `MinimalSidebarView` referencing `TechieDashboardView`). | Cross-layout references are forbidden by the layout contract. Enforced by a `git grep` test in Step 7. |
| Layout views must not modify the global `App.xaml` resource dictionary. | Each layout brings its own theme via `LayoutView.Resources.MergedDictionaries`. The global dictionary is the WPF-UI core only. |

- **T-1 (TitleBar placement) decision:**

  **Empirical test:** create a minimal `UserControl` containing a `<ui:TitleBar>` inside a `ContentControl` inside a `Window`. If the WPF-UI TitleBar renders correctly and its `Close` / `Minimize` / `Maximize` buttons function, it CAN move. If the buttons are dead, it CANNOT move (the WPF-UI `TitleBar` may require direct Window placement to interact with the Win32 window handle).

  **Default assumption (v2):** the WPF-UI TitleBar CANNOT move. The `<ui:TitleBar Title="" Height="56" HorizontalAlignment="Right" Width="140" />` (line 211) stays in `MainWindow.xaml`. The two layout views use a custom `MinimalTitleBarView` / `TechieTitleBarView` UserControl for the rest of the header content (Import button, search, avatar).

  **If T-1 disproves the assumption:** the entire `Grid Grid.Row="0"` (line 210) becomes layout-specific — both layouts have their own complete titlebar, and the WPF-UI TitleBar is removed from `MainWindow.xaml` entirely. This is a **larger** refactor; the plan's risk profile rises.

- **T-9 (SubHero binding) decision:**

  **Current state:**
  - `DashboardViewModel` (lines 68–70) exposes `SubHero1`, `SubHero2`, `SubHero3` as `[ObservableProperty]`.
  - `d184105_DashboardView.xaml` (861 lines) has NO `Tag="SubHero1/2/3"` elements. The references in `.xaml.cs` lines 240–242 are dead.
  - `Views/DashboardView.xaml` (262 lines, Minimal) has NO `Tag="SubHero1/2/3"` elements. The references in `.xaml.cs` lines 29–31 are dead.

  **Decision (v2):** **defer to a 1.4.0 ticket.** The migration does not add SubHero XAML elements. The dead references stay. This is a cosmetic gap, not a compile or runtime blocker.

  **Rationale:** the 861-line Techie XAML has been hand-tuned; adding 3 new sub-hero cards risks visual regression. The properties exist on `DashboardViewModel`; a future ticket can add the cards without touching the migration's commits.

- **M-1 (lifecycle) interactions:**

  | Interaction | Current behaviour | Migration behaviour |
  |-------------|-------------------|---------------------|
  | User clicks "Switch layout" while modal is visible | n/a (no switch in v1) | Modal stays visible across the switch. `LayoutHostViewModel.SwitchLayout` does NOT touch `MainViewModel.IsDialogVisible`. The modal's DataContext remains `MainViewModel` regardless of which layout is active. |
  | User closes the window mid-switch | n/a | `_switchInProgress = true` guard prevents the layout VM from being torn down during a switch. `OnClosing` awaits the switch to complete (or times out at 5s — see Step 6). |
  | App exits via X button | `OnExit` disposes only `UpdateCoordinator` | `OnExit` (D-1, v3 Architecture Review Update) disposes known `IDisposable` components explicitly in registration/declaration order (`UpdateCoordinator` → `MainViewModel` → `TelemetryService` if D-1 Part B approves → future `LayoutHostViewModel` at Step 3A) AFTER the layout VM has detached its `CompositionTarget.Rendering` hook. **Do NOT dispose `IServiceProvider`.** Order: layout `Unloaded` events fire → hook detached → `OnExit` disposes the explicit enumeration. |
  | Layout switch throws (e.g., theme key missing) | n/a | `SwitchLayout` catches the exception, logs to `DiagnosticsService`, raises `LayoutSwitchErrorCount` health metric, reverts the visual state to the previous layout, and does NOT persist the change. |

**Risks**
- The survey doc is a static snapshot. It will become stale. Mitigation: regenerate after Step 6 finishes.
- T-1 empirical test may take 2–4 hours if the WPF-UI TitleBar behaviour is undocumented. Mitigation: budget 1 day for T-1.
- T-9 deferral: team may disagree. If they don't, the migration must add SubHero XAML in Step 5 (~4h extra).

**Validation criteria**
- The survey doc lists every chrome surface, the bindings, and whether it moves.
- T-1 has a written decision with empirical evidence.
- T-9 has a written decision.
- The constraint table is referenced by `_LAYOUT_CONTRACT.md` in Step 1.
- The M-1 interaction table is referenced by Step 4 (switch logic) and Step 6 (shutdown).

**Rollback strategy** Doc-only. Rollback is `git rm`. Zero risk to runtime.

**Estimated complexity** M (4–6h)

**Prerequisites** Step 0.

---

## Step 1 — Scaffolding

**Objective (v3 Architecture Review Update — scoped DOWN to scaffolding only)** Create the layout directory tree and the layout contract doc. **No** `LayoutHostViewModel` placeholder/skeleton is created in this step. **No** Minimal/Techie extraction begins. The app still boots into the current Minimal with zero behavioural change. `LayoutHostView` and `LayoutHostViewModel` are introduced in Step 3A — see forward references below.

**Files affected (new only)** — **v2 uses the `Views/Minimal/`, `Views/Techie/`, `Views/Pages/`, `Views/Widgets/` folder structure (per brief).**
```
src/WallpaperTurbo.UI/Views/
  _LAYOUT_CONTRACT.md                          (doc, no code; codifies the 6 rules + Step 0.5 constraints)
                                                (also: a single empty marker file `.gitkeep` in each of Minimal/, Techie/, Pages/, Widgets/
                                                 to make the folder structure visible in the source tree)
  Minimal/
    .gitkeep                                    (empty folder marker — NO placeholder files for views/VMs)
  Techie/
    .gitkeep                                    (empty folder marker — NO placeholder files for views/VMs)
  Pages/
    .gitkeep                                    (empty folder marker — page views are lifted from MainWindow in Step 3A)
  Widgets/
    .gitkeep                                    (empty folder marker — shared widgets stay in Controls/ for now)
```
- Modified: `src/WallpaperTurbo.UI/ViewModels/MainViewModel.cs` — add `IDisposable` implementation; `Dispose()` unsubscribes from `_telemetryService.MetricsUpdated`. **(D-1 Part A, approved for implementation. Small additive change — does not conflict with the "scaffolding only" constraint: it is a leak fix on an existing VM, not Minimal/Techie extraction.)**
- Modified: `src/WallpaperTurbo.UI/App.xaml.cs` — replace any prior `App.DisposeServices()` reference (none in this plan) with the explicit enumerated disposal list in `OnExit` (see Update 3 in the v3 Architecture Review Update). **Do NOT dispose `IServiceProvider`.**
- New: `tests/WallpaperTurbo.Tests/Layout/MainViewModelDisposalTests.cs` — D-1 Part A test: `MainViewModel_Dispose_UnhooksTelemetry` passes.
- Modified: `src/WallpaperTurbo.UI/WallpaperTurbo.UI.csproj` — no SDK change needed; WPF auto-includes `.xaml`/`.xaml.cs` under the project. **T-2 verification (BG-2 — last gate before Step 1): confirm by running a build.**

**Expected modifications**
- The four new folders (`Views/Minimal/`, `Views/Techie/`, `Views/Pages/`, `Views/Widgets/`) exist with empty `.gitkeep` markers. **No XAML files, no C# files, no `LayoutHostViewModel.cs`, no `LayoutHostView.xaml/.cs` are created in Step 1.** All such files are forward-referenced to later steps:
  - `LayoutHostView.xaml/.cs` and `LayoutHostViewModel.cs` — first introduced in **Step 3A** (real impl, not placeholder).
  - `MinimalLayoutView.xaml/.cs` and `MinimalLayoutViewModel.cs` — first introduced in **Step 3A**.
  - `TechieLayoutView.xaml/.cs`, `TechieLayoutViewModel.cs`, `TechieDashboardView.xaml/.cs`, chrome sub-views — first introduced in **Step 2A** (Techie extraction).
  - `Pages/DashboardView.xaml/.cs`, `Pages/LibraryView.xaml/.cs`, `Pages/SettingsView.xaml/.cs` — first introduced in **Step 3A** (lifted from `Views/*.xaml*`).
  - `Widgets/PerformanceGraph.xaml/.cs`, `Widgets/TelemetryRing.xaml/.cs` — first introduced in **Step 3A** (moved from `Controls/` if exists, else placeholder).
- `MainWindow.xaml` Row 2's `ContentPresenter Content="{Binding CurrentPageViewModel}"` is **not yet** replaced. It still hosts the current `DashboardView`. This step only **adds** the new folders and the D-1 Part A leak fix.
- `MainViewModel.cs` adds `IDisposable` + `Dispose()`. No other change.
- `App.xaml.cs`'s `OnExit` now contains the explicit enumerated disposal list:
  ```text
  App.OnExit
    └─ Dispose known IDisposable components explicitly
       ├─ UpdateCoordinator (already disposed)
       ├─ MainViewModel     (D-1 Part A, approved)
       ├─ TelemetryService  (D-1 Part B, only if T-12 review approves)
       └─ Future LayoutHostViewModel (Step 3A, documented first)
  ```
  Each component is wrapped in a `try/catch` (mirroring the existing `UpdateCoordinator` disposal). **No `App.DisposeServices()` method. No `IServiceProvider.Dispose()`.**
- The `_LAYOUT_CONTRACT.md` codifies:
  - Every layout VM is a singleton.
  - Every layout VM receives its required services via constructor injection (matching today's `MainViewModel` signature style).
  - Layouts never reach across to each other.
  - Layouts never mutate the `MainViewModel` — they bind to it.
  - The `LayoutHostView` is the single entry point `MainWindow` will host in Step 3A.
  - "Layout owns presentation only. Layouts do not own business logic."
  - **v3 ownership (per Architecture Review Update):** `LayoutHostViewModel` is a chrome/container concern only; navigation ownership stays in `MainViewModel`.
  - **v2 constraints (from Step 0.5):** layouts must not declare `IsDialogVisible` etc.; layouts must not host modals; layouts must not subscribe to `MainViewModel` events directly.

**Risks**
- Adding the D-1 Part A `MainViewModel.Dispose()` change touches a file that is central to the app. Mitigation: the change is additive (one new method, one new unsubscribe), localized, and the test in `MainViewModelDisposalTests` catches regressions.
- The explicit `OnExit` enumeration adds 1–4 try/catch blocks. Mitigation: mirror the existing `UpdateCoordinator` pattern exactly; copy-paste the pattern; verify on exit/launch cycle.
- Future architects might be tempted to put business logic in `LayoutHostViewModel`. The contract doc must forbid this.
- **v2 NEW (T-2):** the build may fail if WPF SDK globbing does NOT pick up the new `Views/Minimal/`, `Views/Techie/`, `Views/Pages/`, `Views/Widgets/` paths. **Verify with `dotnet build` after Step 1.** If it fails, add explicit `<Compile Include="Views/Minimal/**/*.xaml" />` and `<Page Include="Views/Minimal/**/*.xaml" />` items to the `.csproj`. (Unlikely but documented as a contingency.)

**Validation criteria**
- Solution builds with 0 warnings, 0 errors (target: same as pre-migration baseline of 0/0 per `0ac1ed0` commit msg).
- `WallpaperTurbo.UI.exe` boots, MainWindow opens, current Minimal dashboard renders. **Zero behavioural change.** No new files referenced at runtime.
- The four new folders exist in the source tree (`.gitkeep` markers visible in `git status`).
- `MainViewModel_Dispose_UnhooksTelemetry` test passes.
- `App.OnExit` does not throw on the new try/catch blocks. The app exits cleanly, no orphaned processes in Task Manager.
- **T-2 (BG-2 — last gate before Step 1):** `dotnet build src/WallpaperTurbo.UI/WallpaperTurbo.UI.csproj -c Release -p:Platform=x64` succeeds.

**Rollback strategy**
- The new folders are net-new (`.gitkeep` markers only). The `MainViewModel.cs` change is additive (one new `IDisposable.Dispose()` method). The `App.xaml.cs` change is additive (extra try/catch blocks in `OnExit`). Rollback: revert the commit. Total risk: low.
- Tag: `git tag pre-step1-rollback` before this step lands.

**Estimated complexity** S (2–3h)

**Prerequisites** Step 0 (incl. T-12 `TELEMETRY_SERVICE_LIFECYCLE.md`, T-9 documented, T-3 lands as a regular task not a blocking gate), BG-1 and BG-2 from the Blocking Gate Items list.

---

## Step 2 — Extract Techie from d184105

**Objective** Move the pre-`0ac1ed0` Techie source from the repo root (`d184105_*.xaml/.xaml.cs`) into the `Views/Techie/` tree created in Step 1, **as-is, unmodified**. Also extract the pre-`0ac1ed0` `MainWindow.xaml` chrome (sidebar + header + footer; **modal overlay stays in MainWindow**) and the pre-`0ac1ed0` `Theme/NeonTechStyle.xaml` for the Techie universe. These become the **Techie baseline** that later steps will refactor.

**Files affected** — **v2 paths (per `Views/Techie/` folder structure):**
- New (extracted from `d184105_*.xaml/.xaml.cs` in root):
  - `src/WallpaperTurbo.UI/Views/Techie/TechieDashboardView.xaml`
  - `src/WallpaperTurbo.UI/Views/Techie/TechieDashboardView.xaml.cs`
- New (extracted from `git show 0ac1ed0^:...`):
  - `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieSidebarView.xaml(.cs)`
  - `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieTitleBarView.xaml(.cs)`
  - `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieFooterView.xaml(.cs)`  (7-metric cyber footer)
  - `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieUpdaterBanner.xaml(.cs)` (extracted from MainWindow)
  - `src/WallpaperTurbo.UI/Theme/Techie/NeonTechStyle.xaml` (the pre-`0ac1ed0` theme)
  - `src/WallpaperTurbo.UI/Theme/Techie/_THEME_KEYS.md` (inventory)
- Deleted (from root): `d184105_DashboardView.xaml`, `d184105_DashboardView.xaml.cs`
- Unchanged: every other file.

**Expected modifications**
- Renaming: `x:Class="WallpaperTurbo.UI.Views.DashboardView"` → `x:Class="WallpaperTurbo.UI.Views.Techie.TechieDashboardView"`. Namespace `WallpaperTurbo.UI.Views` → `WallpaperTurbo.UI.Views.Techie`.
- Namespace renames must be consistent between `.xaml`, `.xaml.cs`, and any `clr-namespace:` XAML references. The pre-`0ac1ed0` file references `clr-namespace:WallpaperTurbo.UI.Controls` and `clr-namespace:WallpaperTurbo.UI.Converters` — these are unchanged and resolve.
- For the chrome extractions (sidebar, titlebar, footer, updater banner), each is **lifted from `MainWindow.xaml`** with its full template and any code-behind. `OnWindowDeactivated` and `OnWindowStateChanged` are NOT chrome — they stay in `MainWindow.xaml.cs`. **The modal overlay (Grid ColumnSpan=2 ZIndex=1000) is NOT extracted** — it stays in `MainWindow.xaml` per the Step 0.5 invariant.
- `Theme/Techie/NeonTechStyle.xaml` keeps the pre-`0ac1ed0` keys verbatim. The minimal theme remains the global default in `App.xaml` (until Step 5 removes it).
- `Theme/Techie/_THEME_KEYS.md` is added to document which keys the Techie chrome and dashboard reference, so a future cleanup pass can verify the file is self-contained.
- The extraction must be **bit-for-bit** with the pre-`0ac1ed0` content. No "while I'm here" cleanups.

**Risks**
- `x:Class` rename means the file will not compile if any other file still references the old type. Mitigation: at this step, no other file references Techie yet — the type is dead-code. Compilation should still succeed.
- The pre-`0ac1ed0` MainWindow.xaml references styles that exist in the pre-`0ac1ed0` NeonTechStyle. If the chrome sub-views are split out before their style dictionary is moved, they will fail to render in design view (but compile fine — keys are `StaticResource` and resolve at runtime). Mitigation: move the theme file FIRST in this step, then move the chrome sub-views.
- The pre-`0ac1ed0` file has hard-coded `xmlns:local`, `xmlns:views`, `xmlns:vm` — these must be retained verbatim.
- The pre-`0ac1ed0` `MainWindow.xaml` uses `clr-namespace:WallpaperTurbo.UI.Views` — when chrome sub-views move out, the `xmlns:views` import will change to `xmlns:techie="clr-namespace:WallpaperTurbo.UI.Views.Techie"` (v2 corrected path).

**Validation criteria**
- Build: 0 warnings, 0 errors.
- The extracted files compile independently (no runtime test required — they're not yet wired).
- `git diff` of the new files vs `git show 0ac1ed0^:...` shows ONLY: (a) `x:Class` rename, (b) namespace rename in code-behind, (c) `xmlns` updates. Nothing else.
- `git show 0ac1ed0 --stat` shows the pre-`0ac1ed0` files; the diff between the pre-`0ac1ed0` source and the extracted files is provably just renames.
- A spot-check of the inertia scroll wiring in `TechieDashboardView.xaml.cs` is present (Lines 1–250 of the d184105 file map to equivalent lines in the new file).

**Rollback strategy**
- All new files are net-new; no source file is deleted in this step (the `d184105_*.xaml/.xaml.cs` root files are deleted only AFTER the new files compile). Rollback: revert commit, root files reappear.
- Tag: `git tag pre-step2-rollback`.

**Estimated complexity** M (6–10h, mostly mechanical file moving + namespace renames + theme file split)

**Prerequisites** Step 1.

---

## Step 2.5 — Settings Diff Analysis (NEW in v2)

**Objective** Diff `SettingsViewModel` (the existing settings surface) against the union of Minimal+Techie settings needs. Produce a settings partition table identifying which settings are: (a) **shared** by both layouts, (b) **minimal-specific**, (c) **techie-specific**. Informs Step 3 (where Minimal and Techie share a baseline `Pages/SettingsView`) and Step 5 (where Techie gets its own `TechieSettingsView` if needed).

**Files affected**
- New: `docs/architecture/SETTINGS_PARTITION.md`
- No source code touched.

**Expected modifications**

- **Current `SettingsViewModel` surface (from `SettingsViewModel.cs`):**

  | Property | Type | Default | Persisted? | Source of truth |
  |----------|------|---------|------------|-----------------|
  | `UseHardwareAcceleration` | bool | true | no (engine state) | `WallpaperService.UseSoftwareDecoding` |
  | `ActivePauseProfile` | string | "Maximized" | no (engine state) | `WallpaperService.ActivePauseProfile` |
  | `SelectedLanguage` | string | "English" | **no (dead state — not read anywhere)** | n/a |
  | `ActiveAppVersion` | string | "v" + `UpdaterViewModel.CurrentVersion` | n/a (read-only display) | Updater |
  | `EngineLogsText` | string | "No logs yet…" | n/a (transient) | `wallpaper.log` |
  | `AutoUpdateEnabled` | bool | true | yes (`JsonUpdaterSettingsStore`) | `IUpdaterSettingsStore` |
  | `CheckOnStartup` | bool | true | yes | `IUpdaterSettingsStore` |
  | `SelectedReleaseChannel` | string | "Stable" | yes | `IUpdaterSettingsStore` |

- **Settings partition table:**

  | Setting | Minimal | Techie | Shared? | Notes |
  |---------|---------|--------|---------|-------|
  | `UseHardwareAcceleration` | yes (in current `SettingsView`) | yes (same) | **shared** | Engine; same control, same binding |
  | `ActivePauseProfile` | yes (ComboBox) | yes | **shared** | Engine; same control |
  | `SelectedLanguage` | **dead state** (not read) | **dead state** | **shared (but dead)** | Either remove in Step 5 or wire it up — out of scope for migration |
  | `ActiveAppVersion` | yes (read-only) | yes | **shared** | Same display |
  | `EngineLogsText` | yes (read-only) | yes | **shared** | Same `wallpaper.log` source |
  | `AutoUpdateEnabled` | yes (ToggleSwitch) | yes | **shared** | Updater |
  | `CheckOnStartup` | yes | yes | **shared** | Updater |
  | `SelectedReleaseChannel` | yes (ComboBox) | yes | **shared** | Updater |
  | **`SelectedLayout` (NEW, Step 6)** | yes (RadioButton) | yes | **shared** | Layout picker (added in Step 6) |
  | **`techieDisabled` (T-3, advanced)** | no | no (applies to both) | **shared** | T-3 feature flag toggle; surface in Settings as "(advanced)" |
  | **`HideFooter` (Techie-specific, optional)** | no | yes (if Techie has the 7-metric footer and user wants to hide it) | **techie-specific** | Out of scope for v1; defer to 1.4.0 |
  | **`TelemetryPulse` (Techie-specific, optional)** | no | yes (if Techie exposes the pulsing `Storyboard`s as toggleable) | **techie-specific** | Out of scope for v1; defer to 1.4.0 |

- **Decision (v2):** `SettingsView` (shared, in `Views/Pages/SettingsView.xaml`) is used by BOTH layouts. Step 5 does NOT introduce a separate `TechieSettingsView.xaml`. If the team later wants Techie-specific settings (Telemetry Pulse, Hide Footer), they can add a `TechieSettingsView` in a follow-up PR. **The current v1 plan's Step 2/5 inclusion of `TechieSettingsView` is dropped in v2.**

- **Why:** keeping the settings view shared reduces the migration's surface area. The Techie layout can render the shared `SettingsView` with Techie tokens (via `LayoutView.Resources.MergedDictionaries`). The visual difference is in the chrome around it, not the content.

**Risks**
- The partition is a snapshot. If the team adds layout-specific settings in 1.4.0, the partition is invalidated. Mitigation: mark the doc as "frozen at migration start" and link to the 1.4.0 ticket.
- "SelectedLanguage" dead state: a code reviewer may flag it. Mitigation: Step 5 includes a "remove dead state" follow-up commit (out of scope here, but documented).

**Validation criteria**
- The partition table is reviewable in < 10 minutes.
- The team agrees (via PR review) that `SettingsView` is shared.

**Rollback strategy** Doc-only. Rollback is `git rm`. Zero risk to runtime.

**Estimated complexity** S (2–3h)

**Prerequisites** Step 0.

---

## Step 3 — Move current UI into MinimalLayoutView

**Objective** Relocate the **current** (Minimal) UI surfaces — `MainWindow` chrome (sidebar, header, updater banner, content presenter; **modal overlay stays in MainWindow**), the three child UserControls (`DashboardView`, `LibraryView`, `SettingsView`), and the `NeonTechStyle` theme — into a `Views/Minimal/` tree, behind a `MinimalLayoutView` shell that owns the chrome. `MainWindow` is reduced to a thin shell hosting `LayoutHostView` (via `Content="{Binding LayoutHost}"`). App still boots into Minimal with **zero behavioural change** to the user.

**v2 decision (NEW):** **`MainShellViewModel` wrapper is dropped.** `MainWindow.DataContext = MainViewModel` (the existing `MainViewModel` stays as the root DataContext). `MainViewModel` is augmented with a new `LayoutHost` property of type `LayoutHostViewModel`. The migration does NOT introduce a `MainShellViewModel.cs` file.

**Files affected** — **v2 paths:**
- New (lifted from `src/WallpaperTurbo.UI/Views/`):
  - `src/WallpaperTurbo.UI/Views/Pages/DashboardView.xaml(.cs)` (verbatim copy, namespace `WallpaperTurbo.UI.Views` preserved for now)
  - `src/WallpaperTurbo.UI/Views/Pages/LibraryView.xaml(.cs)`
  - `src/WallpaperTurbo.UI/Views/Pages/SettingsView.xaml(.cs)`
- New (chrome extractions, namespace `WallpaperTurbo.UI.Views.Minimal.Chrome`):
  - `src/WallpaperTurbo.UI/Views/Minimal/Chrome/MinimalSidebarView.xaml(.cs)`
  - `src/WallpaperTurbo.UI/Views/Minimal/Chrome/MinimalTitleBarView.xaml(.cs)`
  - `src/WallpaperTurbo.UI/Views/Minimal/Chrome/MinimalUpdaterBanner.xaml(.cs)`
- New (the layout shell):
  - `src/WallpaperTurbo.UI/Views/Minimal/MinimalLayoutView.xaml(.cs)` — composes the chrome + `ContentPresenter` bound to `MinimalLayoutViewModel.CurrentPageViewModel`
  - `src/WallpaperTurbo.UI/Views/Minimal/MinimalLayoutViewModel.cs` — `CurrentPageViewModel` property + `NavigateCommand` (lifted from `MainViewModel`)
- New (the host):
  - `src/WallpaperTurbo.UI/Views/LayoutHostView.xaml(.cs)` — the real impl (first introduced in Step 3A; Step 1 scaffolding does not create a placeholder)
  - `src/WallpaperTurbo.UI/Views/LayoutHostViewModel.cs` — the real impl (first introduced in Step 3A; Step 1 scaffolding does not create a placeholder). `ActiveLayout` returns the active layout view. `CurrentLayoutName` defaults to `"Minimal"`. `SwitchLayout` re-evaluates `ActiveLayout`.
- Moved:
  - `src/WallpaperTurbo.UI/Theme/NeonTechStyle.xaml` → `src/WallpaperTurbo.UI/Theme/Minimal/NeonTechStyle.xaml`
- Deleted:
  - `src/WallpaperTurbo.UI/Views/DashboardView.xaml(.cs)`
  - `src/WallpaperTurbo.UI/Views/LibraryView.xaml(.cs)`
  - `src/WallpaperTurbo.UI/Views/SettingsView.xaml(.cs)`
  - `src/WallpaperTurbo.UI/Theme/NeonTechStyle.xaml`
- Modified:
  - `src/WallpaperTurbo.UI/MainWindow.xaml` — Row 1 (left sidebar) and Row 2 (right) **delete content**, replace with single `<ContentControl Content="{Binding LayoutHost}"/>` filling the grid. **The modal overlay (Grid ColumnSpan=2 ZIndex=1000, lines 345–405) STAYS in `MainWindow.xaml`** per the Step 0.5 invariant. The chrome elements (`WindowChrome` settings, `Title="Wallpaper Turbo"`, etc.) and code-behind remain. **The WPF-UI `<ui:TitleBar>` (line 211) stays in `MainWindow.xaml` per the T-1 default assumption (verified in Step 0.5).**
  - `src/WallpaperTurbo.UI/MainWindow.xaml.cs` — `DataContext` continues to point to `MainViewModel` (no change). Icon crop, DWM Mica, Deactivated/Minimized handlers, `OnClosing` remain.
  - `src/WallpaperTurbo.UI/App.xaml` — change the theme `Source` to `Theme/Minimal/NeonTechStyle.xaml`.
  - `src/WallpaperTurbo.UI/App.xaml.cs` — register `LayoutHostViewModel`, `LayoutHostView`, `MinimalLayoutView`, `MinimalLayoutViewModel` as singletons. **Do NOT add a `MainShellViewModel` registration.**
  - `src/WallpaperTurbo.UI/ViewModels/MainViewModel.cs` — add `LayoutHost` property of type `LayoutHostViewModel`. The constructor resolves `LayoutHostViewModel` from DI and assigns it. **No other changes to `MainViewModel`.**

**Expected modifications (precise)**
- The `DataTemplate DataType="{x:Type vm:DashboardViewModel}" → <views:DashboardView />` mappings in `MainWindow.Resources` are moved to `Views/Minimal/MinimalLayoutView.xaml.Resources` and the `views:` prefix becomes `pages:` (or stays as `views:` if the namespace is preserved).
- `MainViewModel` becomes a **pure presentation-orchestrator**: keeps `CurrentPageViewModel`, `NavigateCommand`, `IsEngineRunning`, telemetry bindings, dialog state, import. **Loses nothing functionally.** Adds the new `LayoutHost` sub-property of type `LayoutHostViewModel`.
- **`MainShellViewModel` (v1's wrapper) is NOT introduced.** The `MainViewModel` is the single root DataContext for `MainWindow`. The `LayoutHostViewModel` is a sub-property. This eliminates the wrapper's `LayoutHost` / `IsDialogVisible` forwarding pattern.
- The Minimal theme is relocated to `Theme/Minimal/NeonTechStyle.xaml`. The `App.xaml` `Source` path is updated. **No theme content change.** (Theme scoping to `LayoutView.Resources.MergedDictionaries` happens in Step 5, not here — `App.xaml` still merges the Minimal theme in this step.)
- `LibraryView.xaml.cs` and `SettingsView.xaml.cs` may have small adjustments because their XAML roots' namespaces change. The actual code-behind logic is unchanged.
- `MainWindow.xaml` becomes a thin shell: keeps `WindowChrome`, `Title`, `Icon`, `Height`/`Width`, `WindowStartupLocation`, the WPF-UI `ui:TitleBar` (per T-1), and the modal overlay. The body becomes `<ContentControl Content="{Binding LayoutHost}"/>`.

**Risks**
- **High-risk step.** Multiple resources, namespaces, DataTemplates, and DI registrations are touched simultaneously. The risk of a silent break (e.g., a DataTemplate no longer resolving) is the largest in the whole migration.
- `AncestorType=Window` bindings: the chrome lives inside `MainWindow` directly today. After the move, the chrome lives inside `MinimalLayoutView` which is inside `LayoutHostView` which is inside `ContentControl` which is inside `MainWindow`. The chain still ends at `MainWindow` and `RelativeSource AncestorType=Window` still resolves to the same `MainViewModel`. Verified safe.
- `MainWindow.xaml.Resources` had the `DataTemplate DataType` mappings. They must move to `MinimalLayoutView.xaml.Resources`. If forgotten, navigating to a page will render empty.
- **Modal overlay invariant (v2):** the modal stays in `MainWindow.xaml`. The `MinimalLayoutView` does not host a modal. The modal binds to `MainViewModel.IsDialogVisible` etc. via the `MainWindow.DataContext = MainViewModel` (the layout VM does not own the modal state).
- The user-level Visual Studio designer may not handle this depth of restructuring cleanly. Acceptable — we don't ship designer files.
- DI registration order is critical. Register: services → VMs → `LayoutHostViewModel` → `LayoutHostView` → `MainWindow`.

**Validation criteria**
- Build: 0 warnings, 0 errors.
- App boots. The visual is **pixel-identical** to the pre-Step-3 state (within reason — animations, Mica, and DWM-dependent rendering are out of scope).
- `MainViewModel.IsDialogVisible` toggles → modal appears.
- `MainViewModel.LayoutHost` is non-null and renders `MinimalLayoutView` content.
- All three pages (`Dashboard`, `Library`, `Settings`) navigate correctly.
- Telemetry footer still updates (FPS, VRAM, RAM, Engine state).
- Updater banner still shows/hides.
- Triple-click on Hero card still plays the wallpaper (`Pages/DashboardView.OnCardMouseLeftButtonDown`).
- Window minimize / deactivate still stops the preview service.
- Shutdown still awaits `MainViewModel.ShutdownAsync()`.
- **D-1:** `OnExit` disposes the service provider. No orphaned processes after exit.

**Rollback strategy**
- This step is the **single largest blast radius** in the migration. The strategy is:
  1. Before starting, create tag `git tag pre-step3-rollback`.
  2. Land the move as a **single atomic commit** (no half-states between commits).
  3. If any validation criterion fails, `git revert <step3-commit-sha>`. The pre-step3 code is still in the tag.
  4. If the move is partial, do not ship any further step on top of it — fix the move first.
- **v2:** `MainShellViewModel` is NOT introduced. There is no wrapper. The original `MainViewModel` is the sole root DataContext, with `LayoutHost` as a sub-property (resolved from DI in the constructor). Independent safety comes from the two-tier feature flag (T-3: `WT_DISABLE_TECHIE_LAYOUT` env var + `LocalSettings.techieDisabled` toggle) and per-step `pre-stepN-rollback` tags.

**Estimated complexity** L (24–32h)

**Prerequisites** Steps 1, 2.

---

## Step 3.5 — Layout Switching Mechanism Selection (NEW in v2)

**Objective** Lock in option (a) — `DataTemplate DataType` in `LayoutHostView.Resources`. Document the XAML contract (≤10 lines). Confirm `LayoutHostViewModel` exposes `CurrentLayout` and `Layouts` collections, and `MainViewModel.LayoutHost` is bound from `MainWindow.xaml`.

**Files affected**
- Modified: `src/WallpaperTurbo.UI/Views/LayoutHostView.xaml` — the `LayoutHostView.Resources` now declare the two `DataTemplate DataType` mappings.
- Modified: `src/WallpaperTurbo.UI/Views/LayoutHostViewModel.cs` — exposes `CurrentLayout` and `Layouts` (ObservableCollection<LayoutViewModelBase> or polymorphic type).
- New: `docs/architecture/LAYOUT_SWITCHING_CONTRACT.md` (XAML contract sketch + design rationale).
- No new C# files.

**Expected modifications (XAML contract — ≤10 lines, documented in `LAYOUT_SWITCHING_CONTRACT.md`, NOT implemented in this plan)**

The `LayoutHostView.xaml.Resources` block contains the two `DataTemplate DataType` mappings that resolve the layout VMs to their views. The XAML contract sketch is:

```
<!-- LayoutHostView.xaml.Resources — DataTemplate DataType contract.
     When MainViewModel.LayoutHost.ActiveLayout is a MinimalLayoutViewModel,
     WPF resolves it to a MinimalLayoutView. When it's a TechieLayoutViewModel,
     WPF resolves it to a TechieLayoutView. Scoped to LayoutHostView only. -->
<DataTemplate DataType="{x:Type minimal:MinimalLayoutViewModel}">
  <minimal:MinimalLayoutView />
</DataTemplate>
<DataTemplate DataType="{x:Type techie:TechieLayoutViewModel}">
  <techie:TechieLayoutView />
</DataTemplate>
```

(Implementation detail: also declare `xmlns:minimal="clr-namespace:WallpaperTurbo.UI.Views.Minimal"` and `xmlns:techie="clr-namespace:WallpaperTurbo.UI.Views.Techie"` in `LayoutHostView.xaml`'s root element.)

**Resolution walk (v2 critical detail):** the `<ContentControl Content="{Binding LayoutHost}"/>` in `MainWindow.xaml` needs the `DataTemplate DataType` mapping for `LayoutHostViewModel` → `LayoutHostView` to be reachable from the `ContentControl`'s position in the visual tree. The `ContentControl` is in `MainWindow.xaml`, not in `LayoutHostView.xaml`. WPF walks the visual tree UP for resource resolution. If the `DataTemplate DataType` for `LayoutHostViewModel` is in `LayoutHostView.Resources`, the `ContentControl` (which is a parent of `LayoutHostView`) cannot see it.

**Correct v2 placement:**
- `MainWindow.xaml.Resources` declares `DataTemplate DataType="{x:Type host:LayoutHostViewModel}" → <host:LayoutHostView />` (≤3 lines, in `MainWindow.xaml.Resources`).
- `LayoutHostView.xaml.Resources` declares the two layout-level `DataTemplate DataType` mappings (≤10 lines, in `LayoutHostView.xaml.Resources`).
- Each `LayoutView.xaml.Resources` (MinimalLayoutView, TechieLayoutView) declares the page-level `DataTemplate DataType` mappings (e.g., `DashboardViewModel` → respective `DashboardView`).

**Why this is the right mechanism:**
- WPF resolves `DataTemplate DataType` by walking up the visual tree's `Resources` chain. By placing the layout-level mappings in `LayoutHostView.Resources`, only the `LayoutHostView`'s subtree (which contains one layout at a time) sees them. No cross-contamination.
- The alternative — `DataTemplate DataType` in `MainWindow.Resources` for the layout VMs — would require namespace prefixes for both layouts at the Window level, which leaks the layout concept into `MainWindow`. The host view is the right boundary for layout-level resolution.

**`LayoutHostViewModel` shape (v2):**
- `CurrentLayoutName : string` (default `"Minimal"`) — observable.
- `Layouts : IReadOnlyList<LayoutDescriptor>` — the list of available layouts (Minimal, Techie). Each `LayoutDescriptor` has `Name`, `DisplayName`, `Icon`.
- `ActiveLayout : object` — returns the active `LayoutViewModelBase` instance (either `MinimalLayoutViewModel` or `TechieLayoutViewModel`). Setter is internal; `SwitchLayout` is the public mutation path.
- `SwitchLayout(string name) : void` — the entry point. See Step 6 for the full semantics (apply visually first, persist last, idempotent, re-entrancy guard).
- `LayoutChanged : event EventHandler<LayoutChangedEventArgs>` — raised after a successful switch.

**`MainViewModel.LayoutHost` binding (v2):**
- `MainViewModel.LayoutHost : LayoutHostViewModel` — added in Step 3.
- `MainWindow.xaml` binds `<ContentControl Content="{Binding LayoutHost}"/>` — the `ContentControl`'s `Content` is the `LayoutHostViewModel` instance. WPF resolves the `ContentControl`'s content via `MainWindow.Resources`'s `DataTemplate DataType` for `LayoutHostViewModel`, which instantiates `LayoutHostView`. `LayoutHostView`'s `Resources` resolves the layout VM to a layout view.

**Risks**
- The visual-tree resolution walk is the easiest thing to get wrong. Mitigation: write a Step 3.5 unit test that constructs a `LayoutHostViewModel`, a `LayoutHostView`, sets the `DataContext`, and walks the visual tree to assert the right view is rendered.
- If a future change moves the `ContentControl` outside `MainWindow`, the `DataTemplate DataType` resolution will silently fall back to a default rendering (likely `ToString()` of the VM). Mitigation: the Step 3.5 unit test catches this.

**Validation criteria**
- Build: 0 warnings, 0 errors.
- The XAML contract sketches in this section are committed to `docs/architecture/LAYOUT_SWITCHING_CONTRACT.md` for future reference.
- `LayoutHostViewModel` exposes `CurrentLayoutName`, `Layouts`, `ActiveLayout`, `SwitchLayout`, `LayoutChanged` (5 public members).
- `MainViewModel.LayoutHost` is bound from `MainWindow.xaml`'s `ContentControl` (via `Content="{Binding LayoutHost}"`).
- Unit test: `LayoutHost_ResolvesActiveLayoutToView` passes (constructs the host and walks the visual tree).

**Rollback strategy** Doc-only + small XAML additions. Rollback is `git revert`. Low risk.

**Estimated complexity** S (2–3h)

**Prerequisites** Step 3.

---

## Step 4 — LayoutHost Integration

**Objective** Make `LayoutHostView` the **single point of layout selection**. It reads `LayoutHostViewModel.CurrentLayoutName` and instantiates exactly one of `MinimalLayoutView` or `TechieLayoutView` as its child. Both layouts compile, both can be selected at runtime, but in this step only Minimal is rendered by default.

**Files affected**
- Modified:
  - `src/WallpaperTurbo.UI/Views/LayoutHostView.xaml(.cs)` — hosts a `ContentControl Content="{Binding ActiveLayout}"` whose `DataTemplate DataType` mappings live here (per Step 3.5 contract).
  - `src/WallpaperTurbo.UI/Views/LayoutHostViewModel.cs` — `ActiveLayout` (returns a `MinimalLayoutViewModel` or `TechieLayoutViewModel` based on `CurrentLayoutName`); `CurrentLayoutName` is set from a default at construction (read from `ILayoutPreferenceStore`, fallback to `"Minimal"`); `SwitchLayout(string name)` command updates the name and triggers a re-fetch.
  - `src/WallpaperTurbo.UI/Views/_LAYOUT_CONTRACT.md` — updated to document the Step 3.5 `DataTemplate DataType` contract (≤10 lines).
  - `src/WallpaperTurbo.UI/App.xaml.cs` — register `ILayoutPreferenceStore` (impl) and the singleton for `LayoutHostViewModel`.

**Expected modifications**
- `LayoutHostViewModel.ActiveLayout` switches between the two layout VMs. The implementation is intentionally simple in this step: a backing field that is replaced by a `SwitchLayout` call. No animations, no transition overlays. The window content **teleports** between layouts (acceptable for v1; smooth transitions are explicitly out of scope per the user's "Layout switching implementation details remain flexible").
- `LayoutHostView.xaml.Resources` adds the two `DataTemplate DataType` entries. They are scoped to the `LayoutHostView` only — neither layout view needs to know about the other's data template.
- `MinimalLayoutView` and `TechieLayoutView` are registered as singletons in DI but **never instantiated** until `LayoutHostViewModel` asks for them. The host resolves them via `IServiceProvider` passed into the host VM's constructor.
- No theme dictionary switching happens in this step — both layouts use the global theme that happens to match Minimal. Techie will look **wrong** (using Minimal tokens for Techie shapes) if switched to. This is acceptable: the **switch** works; the **rendering** of Techie is fixed in Step 5. Add a warning log when Techie is selected pre-Step-5.
- **T-3 (NEW):** `LayoutHostViewModel.SwitchLayout` consults `FeatureFlagService.IsTechieLayoutDisabled` (added in Step 0). If true and `name == "Techie"`, log a warning and return without switching. This is the two-tier rollback foundation (outer flag + per-step `pre-stepN-rollback` tag).
- **v2 conservatism (NEW):** the `WM_MOUSEHWHEEL` hook in `TechieDashboardView` is **not refactored** in this step. The current implementation is preserved. **The refactor is on the table, gated on profiling evidence:** Step 4's MEM-02 test (100 switches) measures managed-heap growth. If growth is > 2 MB, Step 5 considers the `WM_MOUSEHWHEEL` refactor. If growth is ≤ 2 MB, Step 5 leaves the hook alone.

**Risks**
- `DataTemplate DataType` resolution order matters. If `LayoutHostView.Resources` declares `DataTemplate DataType="{x:Type minimal:MinimalLayoutViewModel}"`, it must resolve the namespace; `xmlns:minimal` import is needed.
- Memory: switching layout destroys the old visual tree. Any `CompositionTarget.Rendering` or `HwndSource` hook that wasn't cleanly unsubscribed will leak. Mitigation: in this step, switching is via `SwitchLayout` command (manual), so any leak is observable. We add a memory assertion in the test suite (see Phase G).
- The `LayoutHostView` instance is resolved from DI as a singleton. Its `DataContext` is `LayoutHostViewModel`. The `MainViewModel.LayoutHost` property is bound to the `LayoutHostView` instance (or to `LayoutHostViewModel` — decision: bind to the VM; let the View apply itself). Confirm: `MainViewModel.LayoutHost` returns `LayoutHostViewModel`, and the View's `DataContext` is set accordingly.
- During the **first** boot, `LayoutHostViewModel.CurrentLayoutName` has no persisted value. The fallback is `"Minimal"`. After Step 6 adds the preference store, the fallback is the persisted value.

**Validation criteria**
- Build: 0 warnings, 0 errors.
- App boots into Minimal (unchanged behaviour).
- Calling `LayoutHostViewModel.SwitchLayout("Techie")` in a debug menu or test harness swaps the visible content to `TechieLayoutView` (which at this point renders with Minimal tokens — a known visual mismatch).
- Calling `SwitchLayout("Minimal")` restores the previous view.
- Memory snapshot before/after 10 rapid switches: no monotonic growth above a threshold (defined in Phase G).
- `LayoutHostViewModel.SwitchLayout` is idempotent: switching to the active layout is a no-op.
- **T-3:** setting `WT_DISABLE_TECHIE_LAYOUT=1` blocks the switch; unsetting it allows it.
- **MEM-02:** 100 switches do not grow managed heap by more than 2 MB.

**Rollback strategy**
- All changes are inside `Views/LayoutHostView.*`, `Views/LayoutHostViewModel.cs`, and `App.xaml.cs`. The `MainWindow.xaml` is unaffected (no `MainShellViewModel` is involved in Step 4 — v2). Rollback: revert the step-4 commit, the previous "always show Minimal" behaviour returns.

**Estimated complexity** M (8–12h)

**Prerequisites** Step 3.

---

## Step 5 — Refactor Techie hotspots (conservative)

**Objective** Make `TechieLayoutView` render correctly **on its own** theme, with the same set of ViewModels, services, and navigation infrastructure. The Techie look must be "Techie" (cyan + purple glow, 5-button Quick Access, 7-metric cyber footer, "Now Playing" widget, "Current Experience" strobe, 250px sidebar, sidebar Now Playing card, "Quick Access Card" button style, 5-tile dashboard, 4-card playlists). This step does **not** add or change business logic. It only re-binds the Techie visuals to the current VM contract and adjusts the few shapes the current VM doesn't natively expose.

**v2 conservatism (NEW):** Step 5 does **NOT** refactor `DropShadowEffect` choreography, telemetry `Storyboard` pulses, or the inertia scroll in `TechieDashboardView.xaml.cs`. The `WM_MOUSEHWHEEL` hook refactor is **on the table, gated on profiling evidence** from Step 4's MEM-02 test. If MEM-02 shows the current implementation is fine (≤ 2 MB growth over 100 switches), the hook is left alone. If MEM-02 shows growth > 2 MB, the hook refactor is in scope for Step 5.

**Files affected** — **v2 paths:**
- Modified:
  - `src/WallpaperTurbo.UI/Theme/Minimal/NeonTechStyle.xaml` — no content change. The file is the Minimal theme.
  - `src/WallpaperTurbo.UI/Theme/Techie/NeonTechStyle.xaml` — no content change. The file is the Techie theme. **It is referenced by `TechieLayoutView.xaml.Resources.MergedDictionaries` in this step.**
  - `src/WallpaperTurbo.UI/App.xaml` — REMOVE the `Theme/Minimal/NeonTechStyle.xaml` reference. (Both layouts now bring their own theme via `LayoutView.Resources.MergedDictionaries`.) `App.xaml` keeps only the WPF-UI `ThemesDictionary` + `ControlsDictionary`. **This is the per-layout theme dictionary scoping the v2 brief mandates.**
  - `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieSidebarView.xaml(.cs)` — uses `MainViewModel` properties: `IsEngineRunning`, `ActiveWallpaperTitle`, `ActiveWallpaperSpecs`, `IsPlaying`, `PauseCommand`, `PlayCommand`, `StopCommand`, `ActiveRendererText`, `UptimeText`, `FpsText`, `VramText`. All exist. No change.
  - `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieFooterView.xaml(.cs)` — wraps the 7-metric telemetry. All bindings exist on `MainViewModel` (FPS, GPU, VRAM, RAM, Uptime, Resolution, Renderer).
  - `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieTitleBarView.xaml(.cs)` — keeps the existing cyan-glow titlebar. Uses `MainViewModel.ImportWallpaperCommand`. Exists.
  - `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieUpdaterBanner.xaml(.cs)` — same as Minimal's, but styled with Techie tokens.
  - `src/WallpaperTurbo.UI/Views/Techie/TechieDashboardView.xaml` — the 5-button Quick Access row is restored from `d184105`. The SubHero1/2/3 references in the code-behind are **dead code** (per T-9 decision in Step 0.5); they are not used because no XAML element has `Tag="SubHero1/2/3"`. The code-behind compiles cleanly because `DashboardViewModel` exposes the properties.
  - `src/WallpaperTurbo.UI/Views/Techie/TechieDashboardView.xaml.cs` — the inertia scroll code is **kept** as in the d184105 baseline (per v2 conservatism). The triple-click handler dispatch is **kept**. The `WM_MOUSEHWHEEL` hook is **kept** unless MEM-02 evidence justifies a refactor.
  - `src/WallpaperTurbo.UI/Views/Techie/TechieLayoutView.xaml(.cs)` — composes chrome + ContentPresenter + `MergedDictionaries` includes `Theme/Techie/NeonTechStyle.xaml` + `DataTemplate DataType` mappings.
  - `src/WallpaperTurbo.UI/Views/Techie/TechieLayoutViewModel.cs` — `CurrentPageViewModel` and `NavigateCommand`.
  - **No `TechieSettingsView.xaml` (v2 decision, per Step 2.5):** both layouts use the shared `Views/Pages/SettingsView.xaml`.
  - **No `TechieLibraryView.xaml` (v2 decision):** both layouts use the shared `Views/Pages/LibraryView.xaml`. (Verify that the Library view's chrome-independent content renders correctly inside the Techie chrome.)
- New: `src/WallpaperTurbo.UI/Views/Techie/_TECHE_HOTSPOTS.md` — documents every adjustment to the d184105 baseline.

**Expected modifications (precise)**
- `TechieLayoutView.xaml.Resources` includes a `MergedDictionaries` block referencing `../../Theme/Techie/NeonTechStyle.xaml` (relative path; adjust if a build error shows up) and the `DataTemplate DataType="{x:Type vm:DashboardViewModel}" → <techie:TechieDashboardView />` mapping.
- `TechieDashboardView.xaml`: the 5-button Quick Access row keeps all 5 buttons. The sub-hero card area under the hero card is **not** restored (per T-9 deferral). The dead `SubHero1/2/3` references in the code-behind stay (per T-9 decision).
- `TechieDashboardView.xaml.cs`: the inertia scroll code is **kept** as in the d184105 baseline (per v2 conservatism). The triple-click handler dispatch is **kept** (it references `SubHero1/2/3`). The `WM_MOUSEHWHEEL` hook is **kept** unless MEM-02 evidence justifies a refactor. The `RecentlyUsedListBox` `x:Name` is preserved so any external `FindName` callers still resolve.
- `TechieSidebarView`: the "Now Playing" cyber widget with progress bar and play/pause/stop is restored verbatim. `IsPlaying` DataTriggers swap the icon.
- `TechieFooterView`: the 7-metric footer with the "OPTIMIZED / MAX PERFORMANCE" badge and version string is restored verbatim.
- Theme integration: `TechieLayoutView.xaml.Resources` `MergedDictionaries` includes `Theme/Techie/NeonTechStyle.xaml`. The Techie chrome and dashboard reference keys from this dictionary. The Minimal dictionary is **not** loaded in this view.
- `App.xaml` is reduced to:
  ```
  MergedDictionaries:
    ui:ThemesDictionary Theme="Dark"
    ui:ControlsDictionary
  ```
  (No `Source="Theme/.../NeonTechStyle.xaml"` reference.)
- `MinimalLayoutView.xaml.Resources` is updated to include `<ResourceDictionary Source="../../Theme/Minimal/NeonTechStyle.xaml" />` (since `App.xaml` no longer references it).
- `_TECHE_HOTSPOTS.md` records: (a) SubHero1/2/3 references in code-behind are dead code per T-9, (b) no other VM changes, (c) all bindings in Techie chrome resolve to existing `MainViewModel` properties, (d) theme dictionary scoped to `TechieLayoutView.Resources.MergedDictionaries`, (e) no refactor of inertia scroll / `DropShadowEffect` / telemetry pulses (v2 conservatism).

**Risks**
- **Theme collision is the dominant risk.** If both `Theme/Minimal/NeonTechStyle.xaml` and `Theme/Techie/NeonTechStyle.xaml` declare `AccentBrush` (Minimal) and `ActiveCyanBrush` (Techie), and both dictionaries are merged globally, the last-merged wins, and the wrong theme may bleed into both layouts. **Mitigation:** each layout view merges ONLY its own theme in its `Resources.MergedDictionaries`. The global `App.xaml` merge no longer includes either theme — it includes only `Wpf.Ui` core. The global `NeonTechStyle.xaml` reference in `App.xaml` is removed in this step.
- `WindowChrome.IsHitTestVisibleInChrome` is set on titlebar buttons in both layouts. If a layout's titlebar is inside a UserControl that is inside a `ContentControl`, the `WindowChrome.IsHitTestVisibleInChrome` is still respected because it's an inheritable attached property on the `WindowChrome` definition at the `Window` level.
- The Techie DashboardView is verbose (861 lines pre-`0ac1ed0`, restored fully in Step 2). It is a **single UserControl**. There is no attempt to split it into partial classes or components in this step. "Refactor" in this step means: ensure it renders correctly, not: rewrite it.
- **v2 conservatism (NEW):** if the team wants to refactor `DropShadowEffect` / telemetry pulses / inertia scroll, that's a separate ticket. Step 5 will explicitly **refuse** to do so.

**Validation criteria**
- Build: 0 warnings, 0 errors.
- Switching to Techie in dev mode renders the full Techie experience: 250px sidebar, "Now Playing" widget, 5-button Quick Access, hero + 4-card playlists, 7-metric footer, "Current Experience" strobe on the hero, "OPTIMIZED" badge.
- Switching back to Minimal renders the Minimal experience identically to before.
- Inertia scroll on the Recently Used list works (verifies composition-target render loop is properly attached and detached on layout switch).
- A grep test: `git grep "Theme/Techie" src/WallpaperTurbo.UI/Views/Minimal/` returns no results. Theme isolation proven.
- A grep test: `git grep "Theme/Minimal" src/WallpaperTurbo.UI/Views/Techie/` returns no results.
- A grep test: `git grep "Views.Techie" src/WallpaperTurbo.UI/Views/Minimal/` returns no results.
- A grep test: `git grep "Views.Minimal" src/WallpaperTurbo.UI/Views/Techie/` returns no results.
- **v2 NEW:** `App.xaml` no longer contains a `Source="Theme/...NeonTechStyle.xaml"` reference. Verified by reading the file.

**Rollback strategy**
- The only permanent changes outside the Techie tree are: (a) `App.xaml` removes the global `NeonTechStyle` reference, (b) `DashboardViewModel.cs` gains three properties. Rollback: revert commit. Risk: low.
- Tag: `git tag pre-step5-rollback`.

**Estimated complexity** L (20–28h, mostly verification of visual fidelity + the SubHero1/2/3 VM additive change)

**Prerequisites** Step 4.

---

## Step 6 — Settings + Layout Switching

**Objective** Add a **persistent layout preference** so the user can pick Minimal or Techie, the choice survives restart, and the switching is **re-entrant**, **idempotent**, and **transactional** (no half-rendered state, no resource leaks). The user-facing surface is a new `Settings → Layout` section in `Pages/SettingsView`.

**v2 SwitchLayout semantics (NEW):**
1. **Apply visually FIRST, persist LAST (T-4).** The visual swap succeeds before `store.Save` is called. If the swap throws, the persisted value is NOT updated, and the old layout remains.
2. **Idempotent on same name.** `SwitchLayout("Minimal")` when `CurrentLayoutName == "Minimal"` is a no-op (no `LayoutChanged` event, no `store.Save`).
3. **`_switchInProgress` re-entrancy guard (T-6).** A re-entrant call (e.g., from a `LayoutChanged` handler that triggers another `SwitchLayout`) is ignored or queued, never recursive.
4. **Atomic write to `LocalSettings.layout` (T-4).** `JsonLayoutPreferenceStore.Save` writes to a temp file, then renames atomically. A partial write does not corrupt the file.
5. **Catch and log exceptions.** Any exception during the visual swap is caught, logged to `DiagnosticsService`, the `LayoutSwitchErrorCount` metric is incremented, and the old layout remains.
6. **Health metrics (T-7):** `LayoutSwitchDurationMs` and `LayoutSwitchErrorCount` are published to telemetry. **T-7:** they are alertable thresholds (e.g., `LayoutSwitchDurationMs > 2000` raises a warning).

**Files affected**
- New:
  - `src/WallpaperTurbo.Core/Settings/ILayoutPreferenceStore.cs` (interface)
  - `src/WallpaperTurbo.Core/Settings/JsonLayoutPreferenceStore.cs` (impl, file at `%APPDATA%/WallpaperTurbo/layout.json`)
  - `src/WallpaperTurbo.Core/Settings/LayoutPreference.cs` (record: `string CurrentLayout { get; init; }`)
- Modified:
  - `src/WallpaperTurbo.UI/App.xaml.cs` — register `ILayoutPreferenceStore`.
  - `src/WallpaperTurbo.UI/Views/LayoutHostViewModel.cs` — read `CurrentLayout` from the store in the constructor; persist on `SwitchLayout` (LAST, after visual swap; T-4); raise `LayoutChanged` event; add `_switchInProgress` re-entrancy guard (T-6); catch exceptions and increment `LayoutSwitchErrorCount` (T-7); measure `LayoutSwitchDurationMs` (T-7).
  - `src/WallpaperTurbo.UI/Views/Pages/SettingsView.xaml` — add a new "Layout" section with two radio buttons (`Minimal`, `Techie`) and an "Apply" button. (Per Step 2.5, both layouts use the shared `Pages/SettingsView`.)
  - `src/WallpaperTurbo.UI/ViewModels/SettingsViewModel.cs` (the existing one, lifted) — add `SelectedLayout` property + `ApplyLayoutCommand`.
  - `src/WallpaperTurbo.UI/Views/_LAYOUT_CONTRACT.md` (or its successor) — documents the layout preference schema and migration story (existing users default to `Minimal`).
  - `src/WallpaperTurbo.UI/Views/Pages/SettingsView.xaml` Reset button handler — clear layout preference to `Minimal`.
  - `src/WallpaperTurbo.UI/Services/FeatureFlagService.cs` — read `LocalSettings.techieDisabled` toggle (T-3).
  - `src/WallpaperTurbo.UI/Services/TelemetryService.cs` — add `LayoutSwitchDurationMs` and `LayoutSwitchErrorCount` metrics (T-7).

**Expected modifications (precise)**
- `ILayoutPreferenceStore` has `LayoutPreference Load()`, `void Save(LayoutPreference pref)`, default path resolution via `Environment.SpecialFolder.ApplicationData` + `"WallpaperTurbo"` + `"layout.json"`.
- `LayoutPreference` is a `record` with one mutable-by-`with` property: `string CurrentLayout`. Validation: must be `"Minimal"` or `"Techie"`; anything else (corrupt file, missing file) returns `new LayoutPreference { CurrentLayout = "Minimal" }`.
- `LayoutHostViewModel` constructor:
  - Resolve `ILayoutPreferenceStore` from DI.
  - Call `store.Load()` and set `CurrentLayoutName = pref.CurrentLayout`.
  - Compute `ActiveLayout` from the name.
- `LayoutHostViewModel.SwitchLayout(string name)`:
  - Validate name.
  - Set `CurrentLayoutName = name`.
  - If `ActiveLayout` is currently of the **other** type, replace it: release old view, instantiate new view.
  - Persist via `store.Save(new LayoutPreference { CurrentLayout = name })`.
  - Log to `DiagnosticsService` (it exists for this).
  - Raise `LayoutChanged` event.
- Settings view layout section: 2 radio buttons, 1 "Apply" button. The Apply triggers `LayoutHostViewModel.SwitchLayout`. There is **no live preview** — switching is committed, the user sees the new layout, no rolling back to the old.
- **App lifecycle consideration:** the layout is **loaded at boot** based on the persisted preference. The first frame shows the chosen layout, not Minimal. `App.OnStartup` resolves `LayoutHostViewModel` and the layout tree before `MainWindow.Show()`.
- Settings view's Reset button: the existing `ResetAllSettingsAsync` in `SettingsViewModel` is augmented to also call `LayoutHostViewModel.SwitchLayout("Minimal")` and persist.

**Risks**
- **Persist-on-switch ordering.** If `SwitchLayout` persists first and then fails to swap, the persisted value is wrong. **Mitigation:** persist LAST, after the visual swap succeeds. If the swap throws, the old layout remains and the old preference remains.
- **Re-entrancy.** If `LayoutChanged` event handlers trigger another `SwitchLayout` (e.g. telemetry update fan-out), we have infinite recursion. **Mitigation:** `SwitchLayout` is idempotent (checks `CurrentLayoutName` first). Add a `_switchInProgress` guard for re-entrancy. Unit test in Phase G.
- **Resource cleanup on switch.** Switching destroys the old layout's UserControl. `CompositionTarget.Rendering`, `HwndSource.AddHook`, event subscriptions on `MainViewModel`, `DispatcherTimer`s, etc. must be released. The Techie `DashboardView` already correctly releases `CompositionTarget.Rendering` in `OnUnloaded`. The `MainViewModel` event subscription `_telemetryService.MetricsUpdated += OnMetricsUpdated` is owned by `MainViewModel` (singleton) — not the layout. Layouts do **not** subscribe to `MainViewModel` events directly; they bind. Safe.
- **Cross-layout data binding on switch.** If Techie's `TechieDashboardView` had a `Loaded` handler that called `vm.Reload()`, and switching away doesn't call `Unloaded`, the reload may re-occur. **Mitigation:** all "load on first display" logic must be in `OnLoaded`/`OnUnloaded`, never in `DataContextChanged`.
- **File IO failure during save.** `JsonLayoutPreferenceStore.Save` wraps in try/catch and logs to `DiagnosticsService`. Save failure does not block the visual switch.
- **Default preference for existing users.** Anyone with no `layout.json` gets `Minimal`. Documented.

**Validation criteria**
- Build: 0 warnings, 0 errors.
- Set Techie in settings → switch happens visually → close app → reopen → Techie appears.
- Set Minimal → close → reopen → Minimal appears.
- Tamper with `layout.json` to `"Garbage"` → app boots into Minimal (fallback works), no exception surfaces.
- Delete `layout.json` while app is closed → app boots into Minimal, recreates the file with `"Minimal"`.
- Settings "Reset all" returns layout to Minimal.
- 100 rapid switches in a stress test (see Phase G) do not crash, do not leak (memory stabilises), do not corrupt `layout.json`.
- `LayoutHostViewModel.LayoutChanged` event fires exactly once per successful switch.
- **T-3:** with `WT_DISABLE_TECHIE_LAYOUT=1`, `SwitchLayout("Techie")` is a no-op and logs a warning.
- **T-4:** kill the app mid-save (simulated by injecting an exception after `File.WriteAllText` but before `File.Move`); on next launch, the old `layout.json` is intact.
- **T-5:** `git grep "Views.Minimal" src/WallpaperTurbo.UI/Views/Techie/` returns no results (theme isolation; the search is for cross-references, not theme keys themselves).
- **T-6:** a re-entrant `SwitchLayout` from a `LayoutChanged` handler does not enter infinite recursion.
- **T-7:** `LayoutSwitchDurationMs < 2000` is asserted by the test; `LayoutSwitchErrorCount == 0` is asserted.

**Rollback strategy**
- Additive: a new `Core/Settings/` folder plus 3 settings view changes. Rollback: revert commit. The `LayoutHostViewModel` reverts to a `CurrentLayoutName` defaulting to `"Minimal"`.
- Tag: `git tag pre-step6-rollback`.
- **T-8 rollback drill (5 minutes, automated via `docs/architecture/ROLLBACK.md`):**
  1. Set `WT_DISABLE_TECHIE_LAYOUT=1` in the environment.
  2. Restart the app.
  3. Verify: the app boots into Minimal; the Settings view's Layout section shows Techie as disabled.
  4. Set `WT_DISABLE_TECHIE_LAYOUT=0` (or unset) in the environment.
  5. Restart the app.
  6. Verify: the app boots into the persisted layout (Minimal or Techie, whichever was set).
  7. Document the result in `docs/architecture/ROLLBACK.md` (T-8 artifact). The drill proves the two-tier rollback foundation works.

**Estimated complexity** M (10–14h)

**Prerequisites** Step 5.

---

## Step 7 — Testing

**Objective** Build confidence that the migration has not regressed: (a) existing 27/27 unit tests still pass, (b) new tests cover layout switching, navigation, VM↔View binding, and theme isolation, (c) manual test plan is documented and executed, (d) memory & perf baselines are captured.

See **Phase G** below for the full strategy.

**Files affected**
- New tests in `tests/WallpaperTurbo.Tests/`:
  - `Layout/LayoutHostViewModelTests.cs`
  - `Layout/LayoutPreferenceStoreTests.cs`
  - `Layout/NavigationContractTests.cs` (asserts every sidebar entry maps to a VM and View)
- New manual test plan: `docs/testing/LAYOUT_MIGRATION_MANUAL_TESTS.md`
- New perf baseline doc: `docs/testing/PERF_BASELINE.md`

**Estimated complexity** M (12–16h)

**Prerequisites** Step 6.

---

# Phase C — Dependency Ordering

## Hard dependencies (must serialize)

```
Step 0  ─►  Step 1  ─►  Step 2  ─►  Step 3  ─►  Step 4  ─►  Step 5  ─►  Step 6  ─►  Step 7
```

| Edge | Reason it is hard |
|------|-------------------|
| 0 → 1 | The architecture snapshot is the contract that scaffolding must conform to. Without it, scaffolding drifts. |
| 1 → 2 | The `Views/Techie/` tree must exist (created in Step 1) before Techie files are extracted into it. |
| 2 → 3 | The Minimal chrome extraction mirrors what Techie's extraction did; the patterns and naming must be consistent. |
| 3 → 4 | `LayoutHostView` can only resolve `MinimalLayoutView` once `MinimalLayoutView` exists and is wired through `MainViewModel.LayoutHost` (v2: no `MainShellViewModel`). |
| 4 → 5 | Techie can only be visually verified to be "Techie" once the LayoutHost can switch to it. |
| 5 → 6 | Persistence can only persist what the host can switch to. |
| 6 → 7 | Tests are meaningful only once all features exist. |

## Soft dependencies (could parallelize)

| Item A | Item B | Could run in parallel with step |
|-------|--------|---------------------------------|
| `LayoutHostViewModelTests` (test file) | Step 6 implementation | Step 4 (against a test stub) |
| `LayoutPreferenceStoreTests` | Step 6 implementation | Step 1 (against a fake) |
| `_LAYOUT_CONTRACT.md` drafting | Step 1 implementation | Step 0 |
| Perf baseline capture | All steps | Step 7 (after a known baseline is restored) |
| Manual test plan authoring | All steps | Step 0 (durable, evolves) |

## Parallelizable work inside a single step

| Step | Parallelizable sub-tasks |
|------|--------------------------|
| Step 2 | (a) Extract `TechieDashboardView`, (b) Extract `TechieSidebarView`, (c) Extract `TechieFooterView`, (d) ~~Extract `TechieModalOverlay`~~ — **DROPPED in v2** (modal stays in `MainWindow.xaml`), (e) Extract `TechieUpdaterBanner`, (f) Relocate Techie theme. All can be done in parallel branches of a single feature branch and merged together. |
| Step 3 | (a) Extract Minimal sidebar, (b) Extract Minimal titlebar, (c) Extract Minimal updater banner, (d) ~~Extract Minimal modal overlay~~ — **DROPPED in v2** (modal stays in `MainWindow.xaml`), (e) Move Minimal theme, (f) ~~Add `MainShellViewModel`~~ — **DROPPED in v2** (no wrapper; `MainViewModel.LayoutHost` is the integration point). Sub-tasks (a)–(e) can be done in parallel; (f) is a no-op in v2. |
| Step 5 | (a) Refactor Techie chrome bindings, (b) Refactor Techie dashboard bindings, (c) ~~Add SubHero1/2/3 to DashboardViewModel~~ — **DROPPED in v2** (properties already exist on `DashboardViewModel` per T-9; defer XAML elements to 1.4.0). v2 conservatism: do NOT refactor `DropShadowEffect`, telemetry pulses, or inertia scroll. |
| Step 6 | (a) `Core/Settings/ILayoutPreferenceStore` + impl, (b) `LayoutHostViewModel` persistence wiring + re-entrancy guard + health metrics (T-4/T-6/T-7), (c) `Pages/SettingsView` Layout section (shared by both layouts per Step 2.5). (a) blocks (b). (b) and (c) can land in either order or in parallel. |
| Step 7 | (a) Unit tests, (b) Manual test plan, (c) Perf baseline. All three parallel. |

## Critical path

**Step 0 → Step 1 → Step 2 → Step 3 → Step 4 → Step 5 → Step 6 → Step 7.** The single longest path is Steps 3 and 5 (each 20–32h). Total: 88–125h sequential. With the parallelization above, this compresses to ~70–90h of focused engineering time.

## Branching strategy

- All work happens on a **new** branch: `feature/dual-layout-migration`, cut from `feature/app-updater` (current).
- Each step lands as **one commit** (or a small batch of well-named commits) on this branch.
- Each step's pre-rollback tag (`pre-stepN-rollback`) is created on the branch tip before the step's commit.
- Step 3 is the single largest commit. If it must be split for review, split into: **(3a)** `MainWindow` reduced to host `LayoutHost` (with `MainViewModel` as DataContext — no wrapper), **(3b)** Minimal chrome moved into `Views/Minimal/` and `Views/Pages/` (shared). Each sub-step has its own validation. (v2: no `MainShellViewModel` skeleton step.)
- Step 5 may be split into: (5a) `DashboardViewModel` SubHero1/2/3 additive change, (5b) `TechieDashboardView` + `TechieSidebar` + `TechieFooter` refactor + theme scoping, (5c) integration test pass.

---

# Phase D — File-by-File Migration Map

The table below lists every file that the migration creates, modifies, or deletes. "Step introduced" = the step that creates or first touches the file. "Step modified" = the step that finalises the file's role in the dual-layout world. "Step validated" = the step that signs off on the file's correctness.

## Root-level files

| File | Purpose | Step introduced | Step modified | Step validated |
|------|---------|-----------------|---------------|----------------|
| `d184105_DashboardView.xaml` | Techie source-of-truth (pre-`0ac1ed0`) in repo root | n/a (pre-existing) | Step 2 (deleted after extraction) | Step 2 |
| `d184105_DashboardView.xaml.cs` | Techie source-of-truth code-behind | n/a (pre-existing) | Step 2 (deleted after extraction) | Step 2 |
| `AGENTS.md` | Project guidance | n/a (stale, claims to be a skills repo) | n/a (out of scope for this migration — flag separately) | n/a |
| `installer.iss` | Inno Setup script | n/a (empty) | n/a (out of scope) | n/a |

## `src/WallpaperTurbo.UI/`

| File | Purpose | Step introduced | Step modified | Step validated |
|------|---------|-----------------|---------------|----------------|
| `App.xaml` | App-level resource dictionary merge | n/a (pre-existing) | Step 3 (theme path), Step 5 (theme isolated to layouts) | Step 5 |
| `App.xaml.cs` | DI registration, single-instance mutex, startup | n/a (pre-existing) | **Step 1 Scaffolding (v3 Architecture Review Update):** D-1 Part A `MainViewModel` `IDisposable` + `OnExit` explicit enumerated disposal list (UpdateCoordinator → MainViewModel → future LayoutHostViewModel). **No** layout VM registrations in Step 1. Step 3A (adds `LayoutHostViewModel`, `LayoutHostView`, and `MainViewModel.LayoutHost` sub-property; `DataContext = MainViewModel` unchanged), Step 4 (adds `ILayoutPreferenceStore`), Step 6 (registers `ILayoutPreferenceStore` impl) | Step 6 |
| `MainWindow.xaml` | Window shell, WindowChrome, Icon, Mica, modal overlay | n/a (407 lines) | Step 3 (reduced to ~80 lines: chrome, Icon, ContentControl, modal) | Step 3, Step 6 |
| `MainWindow.xaml.cs` | Window code-behind: Icon crop, DWM Mica, Deactivated, OnClosing | n/a (185 lines) | Step 3 (`DataContext = MainViewModel` unchanged; all code-behind logic preserved) | Step 3, Step 6 |
| `WallpaperTurbo.UI.csproj` | Project file | n/a | Step 1 (no SDK change; WPF auto-picks up new `.xaml`) | Step 1 |
| `DebugFlags.cs`, `AssemblyInfo.cs` | Build configuration | n/a | Untouched | Step 0 (verified) |
| `Theme/NeonTechStyle.xaml` | Minimal theme (current) | n/a (348 lines) | Step 3 (moved to `Theme/Minimal/NeonTechStyle.xaml`) | Step 3 |

## `src/WallpaperTurbo.UI/Views/` (revised in v2)

| File | Purpose | Step introduced | Step modified | Step validated |
|------|---------|-----------------|---------------|----------------|
| `_LAYOUT_CONTRACT.md` | Documented contract for all layouts | Step 1 | Step 0.5 (constraint table from survey), Step 3.5 (DataTemplate DataType contract), Step 6 (preference schema) | Step 6 |
| `LayoutHostView.xaml` | Single `ContentControl` hosting active layout | Step 3A (first introduced here — v3 Architecture Review Update removes the Step 1 placeholder) | Step 3A (real impl, `Content="{Binding ActiveLayout}"`), Step 3.5 (DataTemplate DataType in `.Resources`) | Step 4 |
| `LayoutHostView.xaml.cs` | Code-behind for `LayoutHostView` | Step 3A (first introduced here) | Step 3A | Step 4 |
| `LayoutHostViewModel.cs` | `ActiveLayout`, `CurrentLayoutName`, `Layouts`, `SwitchLayout`, `LayoutChanged`, `_switchInProgress` (v3 Architecture Review Update: chrome/container only; navigation stays in `MainViewModel`) | Step 3A (first introduced here — v3 Architecture Review Update removes the Step 1 placeholder) | Step 3A (real impl, no-op SwitchLayout), Step 6 (persistence + event + re-entrancy guard + health metrics) | Step 6 |
| `Pages/DashboardView.xaml(.cs)` | Lifted from `Views/DashboardView.xaml(.cs)` (shared baseline; Minimal uses this) | Step 3 | Step 3 (namespace edits) | Step 3 |
| `Pages/LibraryView.xaml(.cs)` | Lifted from `Views/LibraryView.xaml(.cs)` (shared by both layouts) | Step 3 | Step 3 (namespace edits) | Step 3, Step 6 |
| `Pages/SettingsView.xaml(.cs)` | Lifted from `Views/SettingsView.xaml(.cs)` (shared by both layouts; per Step 2.5 partition) | Step 3 | Step 6 (adds Layout section with radio + Apply) | Step 6 |
| `Minimal/MinimalLayoutView.xaml` | Composes Minimal chrome + ContentPresenter + Minimal theme `MergedDictionaries` | Step 3 | Step 3 (data templates), Step 5 (theme merge) | Step 5 |
| `Minimal/MinimalLayoutView.xaml.cs` | Code-behind for Minimal layout | Step 3 | Step 3 | Step 3 |
| `Minimal/MinimalLayoutViewModel.cs` | `CurrentPageViewModel`, `NavigateCommand` | Step 3 | Step 3 | Step 3 |
| `Minimal/Chrome/MinimalSidebarView.xaml(.cs)` | 240px sidebar with brand, nav, engine card | Step 3 | Step 3 | Step 3 |
| `Minimal/Chrome/MinimalTitleBarView.xaml(.cs)` | 56px search-first header (no WPF-UI TitleBar; that's in MainWindow) | Step 3 | Step 3 | Step 3 |
| `Minimal/Chrome/MinimalUpdaterBanner.xaml(.cs)` | Updater card | Step 3 | Step 3 | Step 3 |
| `Techie/TechieLayoutView.xaml(.cs)` | Composes Techie chrome + ContentPresenter + Techie theme `MergedDictionaries` | Step 2 (placeholder), Step 4 (real) | Step 5 (theme scoping) | Step 5 |
| `Techie/TechieLayoutViewModel.cs` | `CurrentPageViewModel`, `NavigateCommand` | Step 2 (placeholder) | Step 4 (real) | Step 5 |
| `Techie/TechieDashboardView.xaml(.cs)` | Lifted from `d184105_DashboardView.xaml(.xaml.cs)` (layout-specific; uses Techie tokens) | Step 2 | Step 5 (binding check; SubHero references stay dead per T-9) | Step 5 |
| `Techie/Chrome/TechieSidebarView.xaml(.cs)` | 250px sidebar with Now Playing widget | Step 2 | Step 5 (binding check) | Step 5 |
| `Techie/Chrome/TechieTitleBarView.xaml(.cs)` | 64px cyan-glow titlebar (no WPF-UI TitleBar; that's in MainWindow) | Step 2 | Step 5 | Step 5 |
| `Techie/Chrome/TechieFooterView.xaml(.cs)` | 7-metric cyber footer | Step 2 | Step 5 | Step 5 |
| `Techie/Chrome/TechieUpdaterBanner.xaml(.cs)` | Updater card (Techie tokens) | Step 2 | Step 5 | Step 5 |
| `Techie/_TECHE_HOTSPOTS.md` | Documents every adjustment to d184105 baseline (incl. v2 conservatism notes) | Step 5 | Step 5 | Step 5 |
| `Widgets/PerformanceGraph.xaml(.cs)` | Shared performance graph widget (may already exist under `src/WallpaperTurbo.UI/Controls/`) | Step 1 (placeholder if not exists) | Step 3 (move from `Controls/` if exists) | Step 3 |
| `Widgets/TelemetryRing.xaml(.cs)` | Shared telemetry ring widget | Step 1 (placeholder if not exists) | Step 3 (move from `Controls/` if exists) | Step 3 |
| `Theme/Techie/_THEME_KEYS.md` | Inventory of Techie theme keys | Step 2 | Step 2 | Step 5 |

## `src/WallpaperTurbo.UI/ViewModels/`

| File | Purpose | Step introduced | Step modified | Step validated |
|------|---------|-----------------|---------------|----------------|
| `MainViewModel.cs` | Top-level orchestrator (engine, telemetry, dialog, import) | n/a | Step 3 (no change), Step 6 (no change) | Step 3 |
| `LayoutHostViewModel.cs` | Lives in `Views/`, but listed here for reference: `ActiveLayout`, `CurrentLayoutName`, `Layouts`, `SwitchLayout`, `LayoutChanged`, `_switchInProgress`, re-entrancy guard, health metrics (v3 Architecture Review Update: chrome/container only; navigation stays in `MainViewModel`) | Step 3A (first introduced here — v3 Architecture Review Update removes the Step 1 placeholder) | Step 3A (real impl, no-op `SwitchLayout`), Step 6 (persistence + re-entrancy + metrics) | Step 6 |
| `DashboardViewModel.cs` | Dashboard data | n/a | Step 5 (no change; SubHero1/2/3 already exist per T-9) | Step 5 |
| `LibraryViewModel.cs`, `SettingsViewModel.cs`, `UpdaterViewModel.cs` | Other VMs | n/a | Untouched | Step 0 |

## `src/WallpaperTurbo.UI/Views/Pages/` (canonical location post-Step 3)

> v2: In v1 these files were marked for deletion and replacement by per-layout copies. In v2 the **shared** Dashboard, Library, and Settings are kept as the canonical pages in `Views/Pages/`. `Views/Pages/DashboardView` is the baseline for both layouts (Minimal uses it directly; Techie uses `Views/Techie/TechieDashboardView`, a Techie-styled variant of the same content). `Views/Pages/SettingsView` is shared (per Step 2.5 partition). `Views/Pages/LibraryView` is shared.

| File | Purpose | Step introduced | Step modified | Step validated |
|------|---------|-----------------|---------------|----------------|
| `DashboardView.xaml(.cs)` | Shared dashboard page (Minimal uses it; Techie uses its own variant) | Step 3 (lifted from old `Views/DashboardView.xaml(.cs)`) | Step 3 (namespace edits to `Views.Pages`), Step 5 (Techie variant created) | Step 3, Step 5 |
| `LibraryView.xaml(.cs)` | Shared library page | Step 3 (lifted from old `Views/LibraryView.xaml(.cs)`) | Step 3 (namespace edits) | Step 3, Step 6 |
| `SettingsView.xaml(.cs)` | Shared settings page (used by BOTH layouts per Step 2.5) | Step 3 (lifted from old `Views/SettingsView.xaml(.cs)`) | Step 6 (adds Layout section) | Step 6 |

## `src/WallpaperTurbo.UI/Services/`, `Controls/`, `Converters/`, `Assets/`

Untouched throughout the migration. They are referenced by both layouts.

## `src/WallpaperTurbo.Core/`

| File | Purpose | Step introduced | Step modified | Step validated |
|------|---------|-----------------|---------------|----------------|
| `Settings/ILayoutPreferenceStore.cs` | Layout preference persistence interface | Step 6 | Step 6 | Step 6 |
| `Settings/JsonLayoutPreferenceStore.cs` | File-based impl | Step 6 | Step 6 | Step 6 |
| `Settings/LayoutPreference.cs` | Record holding `CurrentLayout` | Step 6 | Step 6 | Step 6 |
| All other Core files | Backend, services, models | n/a | **Untouched** | Step 0 |

## `src/WallpaperTurbo.Updater/`, `src/WallpaperTurbo.AppRunner/`, etc.

Untouched throughout the migration. They are referenced by the UI's `App.xaml.cs` DI graph and have no UI surfaces.

## `tests/WallpaperTurbo.Tests/`

| File | Purpose | Step introduced | Step modified | Step validated |
|------|---------|-----------------|---------------|----------------|
| Existing 6 test files | Updater, versioning | n/a | Untouched (must stay green) | Step 0 |
| `Layout/LayoutHostViewModelTests.cs` | `SwitchLayout`, persistence, re-entrancy | Step 7 | Step 7 | Step 7 |
| `Layout/LayoutPreferenceStoreTests.cs` | Read/write, corrupt file, missing file | Step 7 | Step 7 | Step 7 |
| `Layout/NavigationContractTests.cs` | Asserts every VM↔View mapping is intact | Step 7 | Step 7 | Step 7 |

## `docs/`

| File | Purpose | Step introduced | Step modified | Step validated |
|------|---------|-----------------|---------------|----------------|
| `architecture/NAVIGATION_SNAPSHOT.md` | Step 0 output | Step 0 | Step 0 | Step 0 |
| `architecture/VM_CAPABILITY_MATRIX.md` | Step 0 output | Step 0 | Step 0 | Step 0 |
| `architecture/SHARED_BACKEND_INVARIANTS.md` | Step 0 output | Step 0 | Step 0 | Step 0 |
| `architecture/DUAL_LAYOUT_MIGRATION_PLAN.md` | This file | n/a | Continuously updated through Step 7 | Step 7 |
| `testing/LAYOUT_MIGRATION_MANUAL_TESTS.md` | Step 7 manual test plan | Step 7 | Step 7 | Step 7 |
| `testing/PERF_BASELINE.md` | Step 7 perf baseline | Step 7 | Step 7 | Step 7 |

---

# Phase E — LayoutHost Integration Strategy

## Current flow (pre-migration)

```
App.OnStartup
  └── ConfigureServices()           [App.xaml.cs: 27-79]
        └── services.AddSingleton<MainViewModel>(...)
        └── services.AddSingleton<MainWindow>(...)
  └── _serviceProvider.GetRequiredService<MainWindow>()
  └── mainWindow.Show()
        └── MainWindow.xaml.cs:21
              └── DataContext = viewModel  (the MainViewModel)
        └── MainWindow.xaml:25-35
              └── <Window.Resources>
                    └── <DataTemplate DataType="{x:Type vm:DashboardViewModel}">
                          <views:DashboardView />
                        </DataTemplate>
                        (likewise for Library, Settings)
                    </Window.Resources>
        └── MainWindow.xaml:340-342
              └── <ContentPresenter Grid.Row="2" Content="{Binding CurrentPageViewModel}" />
              └── CurrentPageViewModel comes from MainViewModel.NavigateCommand
```

**The MainWindow owns the chrome** (sidebar, header, banner, footer) and the modal overlay, AND it hosts the page navigation via the ContentPresenter + Window.Resources DataTemplates. **There is no LayoutHost concept.**

## Future flow (post-migration)

```
App.OnStartup
  └── ConfigureServices()
        └── ... existing services ...
        └── services.AddSingleton<ILayoutPreferenceStore, JsonLayoutPreferenceStore>()
        └── services.AddSingleton<FeatureFlagService>()      // v2: T-3 feature flag
        └── services.AddSingleton<LayoutHostViewModel>()
        └── services.AddSingleton<MainWindow>()
  └── mainWindow.Show()
        └── MainWindow.xaml.cs
              └── DataContext = mainViewModel   // v2: NOT mainShellViewModel; wrapper dropped
        └── MainWindow.xaml (reduced to ~80 lines)
              └── <WindowChrome> + <Title> + <Icon> + <Height>/<Width>
              └── <Grid>
                    └── <ContentControl Content="{Binding LayoutHost}" />   <!-- resolves LayoutHostViewModel -->
                    └── <ModalOverlay IsDialogVisible={Binding IsDialogVisible} ... />
        └── ContentControl resolves LayoutHost via DataTemplate DataType
              └── LayoutHostView (UserControl) is its Content
                    └── LayoutHostView.xaml.Resources declares:
                          DataTemplate DataType="{x:Type minimal:MinimalLayoutViewModel}" → <minimal:MinimalLayoutView />
                          DataTemplate DataType="{x:Type techie:TechieLayoutViewModel}" → <techie:TechieLayoutView />
                    └── ContentControl Content="{Binding ActiveLayout}"
        └── ActiveLayout is one of MinimalLayoutView or TechieLayoutView
              └── Each layout view has its own ContentPresenter bound to its CurrentPageViewModel
                    └── Each layout view's Resources declares:
                          DataTemplate DataType="{x:Type vm:DashboardViewModel}" → <minimal:MinimalDashboardView /> (in Minimal)
                          DataTemplate DataType="{x:Type vm:DashboardViewModel}" → <techie:TechieDashboardView /> (in Techie)
              └── Each layout view merges its own theme dictionary in its Resources.MergedDictionaries
```

**The MainWindow no longer owns the chrome.** It owns: WindowChrome (caption area, hit testing), Window-level state (Mica, DWM), the modal overlay, the icon. The LayoutHostView is the body. Each layout is a self-contained universe: chrome + theme + page views.

## Ownership matrix (post-migration)

| Concern | Owner | Why |
|---------|-------|-----|
| Window chrome (`WindowChrome`) | MainWindow | OS-level hit testing must be on the Window |
| Window state (Maximized, Minimized) | MainWindow | Affects Window, not layout |
| Mica, DWM dark mode | MainWindow | Platform integration |
| Icon crop, DWM attribute calls | MainWindow | Platform integration |
| Deactivated/Minimized → preview cancel | MainWindow | Affects the Window lifetime, not layout |
| `ShutdownAsync` (graceful close) | MainWindow | Closes the Window |
| Modal overlay (Z=1000) | MainWindow | Must overlay every layout |
| Layout selection (Minimal vs Techie) | LayoutHostViewModel | The host decides |
| Active layout (the actual rendered chrome + page) | LayoutHostView | Visual host |
| Layout-scoped theme dictionary | LayoutHostView.Resources | Each layout brings its own |
| Layout chrome (sidebar, header, footer) | MinimalLayoutView / TechieLayoutView | Each layout owns its visuals |
| Layout-scoped DataTemplate mappings | MinimalLayoutView.Resources / TechieLayoutView.Resources | Each layout binds its VMs to its views |
| Page navigation (Dashboard, Library, Settings) | MinimalLayoutViewModel / TechieLayoutViewModel | The active layout navigates |
| Engine state, telemetry, import, dialog state, navigation, settings persistence | MainViewModel | Backend logic, layout-agnostic |
| Dashboard data, Library data, Settings data, Updater data | DashboardViewModel, LibraryViewModel, SettingsViewModel, UpdaterViewModel | Backend logic, layout-agnostic |
| Layout preference persistence | ILayoutPreferenceStore | Backend, layout-agnostic |

## Migration risks during this transition

| Risk | Mitigation |
|------|------------|
| Two `DataTemplate DataType="{x:Type vm:DashboardViewModel}"` — one in Minimal, one in Techie — both in scope if both layouts are loaded. | Each layout's DataTemplate is scoped to that layout's `Resources`. The other layout's tree never sees them. WPF's resource resolution walks up the visual tree, so the inner-scope template wins for its own view. Verified by grep test in Phase G. |
| `AncestorType=Window` in Techie sub-views resolves to the wrong `DataContext` once nested in `LayoutHostView > TechieLayoutView > TechieDashboardView`. | WPF walks the visual tree, not the logical tree, for `AncestorType`. The Window is the root. The DataContext chain at the Window is `MainViewModel`. The layout VMs do not need to forward data; they bind directly to the MainViewModel's properties. |
| Modal overlay at `ZIndex=1000` is in MainWindow's grid — it must overlay both layouts. | The modal lives in MainWindow, not in the LayoutHost. The ContentControl hosting the layout is at grid row 0, the modal is at row 0 with `Panel.ZIndex=1000`. WPF renders the modal on top. |
| `WindowChrome.IsHitTestVisibleInChrome` on titlebar buttons inside a UserControl inside a ContentControl. | The `WindowChrome` is defined on the `Window`. The attached property `WindowChrome.IsHitTestVisibleInChrome="True"` is honoured anywhere in the visual tree. Confirmed by WPF semantics. |
| Memory leaks when switching layouts (composition target rendering, hwnd source hook, dispatcher timer). | The Techie `DashboardView.OnUnloaded` already removes the rendering hook. We add a Step 4 memory test (Phase G) that asserts 100 switches do not grow managed heap. |

## Regression avoidance checklist (run after every commit between Step 3 and Step 7)

1. App boots. MainWindow opens. Layout is the persisted one (or Minimal if none).
2. Sidebar nav clicks navigate to Dashboard / Library / Settings.
3. Each of the 3 pages renders.
4. Engine toggle button works (start/stop wallpaper).
5. Telemetry footer shows live values.
6. Updater banner appears when there's an update.
7. Modal dialog appears on confirm/cancel prompts.
8. Window minimize stops preview.
9. Window deactivate stops preview.
10. Triple-click on hero card plays the wallpaper.
11. Settings reset returns to defaults + Minimal layout.
12. Close app, reopen, layout is preserved.

---

# Phase F — Techie Restoration Strategy

## Safest extraction sequence (already described in Step 2)

```
Phase 2.1  Verify pre-0ac1ed0 source via git show
Phase 2.2  Create Theme/Techie/NeonTechStyle.xaml (pre-0ac1ed0 theme)
Phase 2.3  Create TechieDashboardView.xaml(.cs) from d184105 root file
Phase 2.4  Create TechieSidebarView, TechieTitleBarView, TechieFooterView,
           TechieModalOverlayView, TechieUpdaterBanner from pre-0ac1ed0 MainWindow
Phase 2.5  Delete d184105_*.xaml* from root (only after step 2.3 compiles)
```

## Files to restore first

1. **Theme first** — `Theme/Techie/NeonTechStyle.xaml`. Without it, the chrome sub-views' style keys don't resolve. Even though the views are not yet wired, having the theme in place makes any subsequent compile of an extracted sub-view succeed.
2. **DashboardView second** — it's the largest single piece of Techie's identity and the most complex (inertia scroll, triple-click dispatch, 3 sub-heroes). Restoring it first surfaces the SubHero1/2/3 gap early.
3. **Chrome sub-views third** — sidebar, titlebar, footer. Each is a small lift with clear bindings. Restoring them in any order is fine; do them in one commit.
4. **Updater banner and modal overlay last** — they are shared with Minimal and have no Techie-specific surprises.

## Files to restore last

The `TechieLibraryView` and `TechieSettingsView` — these are smaller, less distinctive, and depend on the chrome being in place. They are also the surfaces that gain the most from Step 6 (the Layout section in Settings).

## Isolation strategy

| Boundary | How it's enforced |
|----------|-------------------|
| **Code-behind** | Techie code-behind lives in `Views/Techie/`. It cannot be referenced from `Views/Minimal/`. Enforced by `git grep` test in Phase G: `git grep "Views.Techie" src/WallpaperTurbo.UI/Views/Minimal/` returns no results. |
| **XAML resources** | Techie theme is referenced only by `TechieLayoutView.Resources` (and sub-views). It is **not** in `App.xaml`. Minimal theme is in `Theme/Minimal/`, referenced by `MinimalLayoutView.Resources` and `App.xaml.MergedDictionaries` (because Minimal is the default). When a layout is rendered, only that layout's merged dictionaries are in scope for the visual tree. |
| **DataTemplates** | `DataTemplate DataType="{x:Type vm:DashboardViewModel}"` appears once in `MinimalLayoutView.Resources` (pointing to `MinimalDashboardView`) and once in `TechieLayoutView.Resources` (pointing to `TechieDashboardView`). Neither layout's `Resources` references the other's view. |
| **ViewModels** | All VMs are shared. No layout-specific code is added to the VMs except the 3 read-only SubHero properties in Step 5. SubHero1/2/3 are documented as "view-state derived from RecentlyUsedWallpapers; populated for layouts that need preview slots." Minimal ignores them. |
| **MainViewModel** | Untouched. Layouts bind to its public surface. |

## How to avoid contaminating Minimal

1. **No changes to `Views/` or to `Views/Minimal/Chrome/` files during Step 2.** Step 2 is exclusively about creating new files under `Views/Techie/` and `Theme/Techie/`.
2. **No changes to `App.xaml` during Step 2.** The Minimal theme stays as the global default.
3. **No changes to `App.xaml.cs` during Step 2.** No new DI registrations.
4. **The `d184105_*.xaml*` files in the root are deleted only after `Views/Techie/TechieDashboardView.xaml(.cs)` compiles and is byte-equivalent (modulo renames).** This is verified by a `git show 0ac1ed0^:...` diff.

## Extraction rules (per file, no exceptions)

- **Bit-for-bit** with the pre-`0ac1ed0` source. The only allowed edits are:
  - `x:Class` rename to the new namespace.
  - `xmlns:` import path updates.
  - Code-behind `namespace` and `using` updates.
- **No "while I'm here" fixes.** A typo discovered during extraction is filed as a follow-up; it does NOT get fixed in Step 2. The migration is not a refactor.
- **No deletions of redundant code.** A redundant style or an unused brush in the pre-`0ac1ed0` file is kept verbatim. Cleanup happens in Step 5's "refactor Techie hotspots" pass, which is its own commit.
- **No new files added during Step 2 that are not in the pre-`0ac1ed0` universe.** The restoration is a snapshot, not a redesign.

## Order of Techie-specific files in the new tree (v2: `Views/Techie/`; no `TechieSettingsView`/Techie modal)

```
src/WallpaperTurbo.UI/Views/Techie/
  TechieLayoutView.xaml(.cs)               (composes chrome + ContentPresenter; placeholder in Step 2, real in Step 4)
  TechieLayoutViewModel.cs                 (placeholder in Step 2)
  TechieDashboardView.xaml(.cs)            (restored in Step 2.3, refactored in Step 5)
  Chrome/
    TechieSidebarView.xaml(.cs)            (restored in Step 2.4)
    TechieTitleBarView.xaml(.cs)           (restored in Step 2.4)
    TechieFooterView.xaml(.cs)             (restored in Step 2.4)
    TechieUpdaterBanner.xaml(.cs)          (restored in Step 2.4)
  _TECHE_HOTSPOTS.md                       (Step 5 only)
```

> v2: `TechieModalOverlayView.xaml(.cs)` is NOT created — modal stays in `MainWindow.xaml`. `TechieSettingsView.xaml(.cs)` is NOT created — both layouts use the shared `Views/Pages/SettingsView` (per Step 2.5). `TechieLibraryView.xaml(.cs)` is `Step 2 placeholder, Step 5 real`; the canonical `LibraryView` lives in `Views/Pages/LibraryView` (shared).

---

# Phase G — Testing Strategy

For each test category: **purpose**, **scope**, **failure indicators**.

## G.1 Unit tests (xUnit, in `tests/WallpaperTurbo.Tests/`)

| Test class | Purpose | Scope | Failure indicators |
|------------|---------|-------|---------------------|
| `LayoutHostViewModelTests` | `SwitchLayout` idempotence, re-entrancy guard, persistence, event firing | The host VM in isolation, against a fake `ILayoutPreferenceStore` and a fake `IServiceProvider` returning two layout VMs | (a) `SwitchLayout` with same name is a no-op, (b) `SwitchLayout` with different name fires `LayoutChanged` exactly once, (c) Re-entrant call (`LayoutChanged` handler triggers another `SwitchLayout`) is guarded by `_switchInProgress`, (d) Successful switch calls `store.Save` once with the new name, (e) `SwitchLayout` to invalid name throws `ArgumentException` |
| `LayoutPreferenceStoreTests` | Read, write, corrupt file, missing file, default | `JsonLayoutPreferenceStore` against a temp directory | (a) Missing file returns `new LayoutPreference { CurrentLayout = "Minimal" }`, (b) Valid file returns its content, (c) File with `{"CurrentLayout":"Techie"}` returns Techie, (d) File with `{"CurrentLayout":"Garbage"}` returns Minimal (fallback), (e) `Save` is atomic (write-to-temp-then-move, or a documented equivalent), (f) Save failure (read-only directory) does not throw — logs to `DiagnosticsService` |
| `NavigationContractTests` | Verify every sidebar entry maps to a VM and View | Reflection over `MainViewModel.NavigateCommand` and the `DataTemplate DataType` registrations | (a) `Navigate("Dashboard")` sets `CurrentPageViewModel` to `DashboardViewModel`, (b) The Dashboard DataTemplate is registered in `MinimalLayoutView.Resources` and `TechieLayoutView.Resources`, (c) `Navigate("Library")` and `Navigate("Settings")` likewise, (d) Unknown destinations are no-ops (current behaviour) |
| `SubHeroPropertyTests` | Verify SubHero1/2/3 surface | `DashboardViewModel` with a stubbed `RecentlyUsedWallpapers` | (a) Empty list → all three are `null`, (b) List of N items → SubHero1 = items[0], SubHero2 = items[1] (or null if N < 2), SubHero3 = items[2] (or null if N < 3), (c) `OnRecentlyUsedWallpapersChanged` raises `PropertyChanged` for all three |
| `LayoutHostReentrancyTests` (v2) | Verify the re-entrancy guard and health metrics | `LayoutHostViewModel` with stubbed `ILayoutPreferenceStore` and `LayoutChanged` handlers that re-invoke `SwitchLayout` | (a) Re-entrant `SwitchLayout` from a `LayoutChanged` handler is suppressed by `_switchInProgress`, (b) After the outer call completes, a fresh `SwitchLayout` succeeds, (c) `LayoutSwitchErrorCount` increments when an exception is thrown inside `SwitchLayout`, (d) `LayoutSwitchDurationMs` is non-negative for every successful switch |
| `FeatureFlagTests` (v2) | Verify the Techie feature flag gates `SwitchLayout` | `FeatureFlagService` and `LayoutHostViewModel` with toggled `LocalSettings.techieDisabled` | (a) With `techieDisabled = true`, `SwitchLayout("Techie")` is a no-op and logs to `DiagnosticsService`, (b) With `techieDisabled = false`, `SwitchLayout("Techie")` succeeds, (c) `WT_DISABLE_TECHIE_LAYOUT=1` env var is honoured on app start (clears any persisted Techie preference) |
| `ThemeIsolationTests` (grep-based, v2) | Verify layouts don't cross-reference each other's theme/view types | Repository-wide `git grep` over the relevant trees | (a) `git grep "Views.Techie" src/WallpaperTurbo.UI/Views/Minimal/` returns no results, (b) `git grep "Views.Minimal" src/WallpaperTurbo.UI/Views/Techie/` returns no results, (c) `git grep "Theme/Techie" src/WallpaperTurbo.UI/Views/Minimal/` returns no results, (d) `git grep "Theme/Minimal" src/WallpaperTurbo.UI/Views/Techie/` returns no results |

## G.2 Integration tests

| Test | Purpose | Scope | Failure indicators |
|------|---------|-------|---------------------|
| `LayoutSwitchingIntegrationTest` | Verify the full `LayoutHostView → ActiveLayout` round-trip | Boots the `LayoutHostView` in a hidden `Window`, calls `SwitchLayout`, asserts the visual tree type | (a) After `SwitchLayout("Techie")`, the `ContentControl.Content` is of type `TechieLayoutView`, (b) After `SwitchLayout("Minimal")`, it is `MinimalLayoutView`, (c) The `MainViewModel.IsEngineRunning` is still bound correctly after a switch, (d) The `Updater` property of `MainViewModel` still drives the `UpdaterBanner` visibility after a switch |
| `DataTemplateResolutionTest` | Verify that when a `DashboardViewModel` is presented inside `MinimalLayoutView`, it resolves to `MinimalDashboardView`, and inside `TechieLayoutView`, to `TechieDashboardView` | Constructs each layout view, sets `CurrentPageViewModel = new DashboardViewModel()`, walks the visual tree, asserts the leaf type | (a) Minimal scope resolves to `MinimalDashboardView`, (b) Techie scope resolves to `TechieDashboardView`, (c) When both layouts are alive simultaneously (rare but possible in test harness), each resolves to its own view (no cross-contamination) |
| `PersistenceRoundTripTest` | Verify save/load round-trip preserves the user's choice | Writes a `JsonLayoutPreferenceStore` to a temp dir, then constructs a new instance against the same dir | (a) Save `"Techie"`, then load → returns Techie, (b) Save `"Minimal"`, then load → returns Minimal |
| `SettingsViewLayoutApplyTest` | Verify clicking "Apply" in the Settings view triggers a switch | Boots the Settings view, invokes the `ApplyLayoutCommand` for "Techie" | (a) After `Apply`, `LayoutHostViewModel.CurrentLayoutName` is "Techie", (b) The persistence store has the new value, (c) The `LayoutChanged` event fired exactly once |

## G.3 Manual tests (recorded in `docs/testing/LAYOUT_MIGRATION_MANUAL_TESTS.md`)

| Test | Purpose | Scope | Failure indicators |
|------|---------|-------|---------------------|
| MT-01 App boots into persisted layout | End-to-end smoke | Set Techie in settings, close, reopen | Window renders Techie chrome, not Minimal |
| MT-02 First-boot default | Existing-user experience | Delete `layout.json`, boot | Window renders Minimal |
| MT-03 Switch via settings | End-to-end UX | Open Settings, choose Techie, click Apply | Window content swaps to Techie |
| MT-04 All three pages render in Techie | Catch dashboard-only tests | Switch to Techie, click each of Dashboard / Library / Settings in the sidebar | Each page renders without exception, telemetry footer updates, no missing-resource warnings in `myeasylog.log` |
| MT-05 Triple-click sub-hero in Techie | Verify SubHero bindings | Switch to Techie, triple-click sub-hero 1 | The corresponding wallpaper plays |
| MT-06 Inertia scroll in Techie | Verify CompositionTarget hook is properly attached/detached | Switch to Techie, scroll the Recently Used list with a precision touchpad | Smooth glide, no stutter, no exceptions when switching away mid-scroll |
| MT-07 Layout survives restart | Persistence | Set Techie, close, reopen | Techie appears |
| MT-08 Corrupt preference file | Robustness | Set Techie, close, manually edit `layout.json` to `"Garbage"`, reopen | App boots into Minimal, logs the corrupt value to `myeasylog.log`, no exception |
| MT-09 Engine toggle works in both layouts | Backend integration | Toggle engine in Minimal, then in Techie | Engine starts/stops, telemetry updates, "Now Playing" widget (Techie) and "Resume/Pause" card (Minimal) both reflect state |
| MT-10 Updater banner in both layouts | Backend integration | Trigger a fake update notification | Banner appears in both layouts with the correct tokens |
| MT-11 Modal dialog in both layouts | Modal overlay | Trigger the install-update confirmation dialog | Dialog overlays both layouts, Cancel/Confirm work |
| MT-12 Settings reset returns to Minimal | Settings integration | Set Techie, then Reset All in settings | Layout returns to Minimal, persisted file says "Minimal" |
| MT-13 Sidebar 250px vs 240px | Visual contract | Measure sidebar width in both layouts | Techie: 250px, Minimal: 240px |
| MT-14 Footer 7-metric vs none | Visual contract | Inspect footer in both layouts | Techie: 7 metrics + Optimized badge, Minimal: no footer (engine status in sidebar) |
| MT-15 Theme isolation | Visual contract | In each layout, hover over a neon-card border and a button | Hover effect uses the correct accent (cyan+purple in Techie, single accent in Minimal) |
| MT-16 High-DPI rendering | Display integration | Run on a 4K monitor at 200% DPI | No blur, no scaling artefacts (Techie's text de-blur was the original 0ac1ed0 reason) |
| MT-17 Multi-monitor | Display integration | Move window between monitors with different DPI | No layout regressions |

## G.4 Memory tests (in `tests/WallpaperTurbo.Tests/` with `BenchmarkDotNet` or `dotMemory`)

| Test | Purpose | Scope | Failure indicators |
|------|---------|-------|---------------------|
| MEM-01 Single switch | Baseline | Switch Minimal → Techie → Minimal once | Working set does not grow by more than 5 MB |
| MEM-02 100 switches | Stress | Switch 100 times alternating | Managed heap size does not grow by more than 2 MB; Gen 0/1/2 collections occur as expected |
| MEM-03 Techie-only | Techie stress | Switch to Techie, leave 5 min, switch to Minimal | CompositionTarget.Rendering handler is detached (verified by reflection on the static `Rendering` event) |
| MEM-04 No `HwndSource` leak | Win32 | Switch 50 times | Number of `HwndSource` instances attached to the WPF `HwndSource` collection does not grow monotonically |
| MEM-05 No event subscription leak | Events | Switch 100 times | `_telemetryService.MetricsUpdated` invocation list count remains ≤ 1 (only `MainViewModel.OnMetricsUpdated`) |

## G.5 Navigation tests

| Test | Purpose | Scope | Failure indicators |
|------|---------|-------|---------------------|
| NAV-01 Sidebar nav | Each entry navigates | Click each wired entry (`Dashboard`, `Library`, `Settings`) in both layouts | `CurrentPageViewModel` updates to the expected VM type |
| NAV-02 Unwired entries no-op | Documented behaviour | Click `Playlists`, `Monitor setup`, `Engine`, `Performance`, `About` | No change to `CurrentPageViewModel`, no exception |
| NAV-03 Default page is Dashboard | First-frame | Open window | `CurrentPageViewModel` is `DashboardViewModel` on the first frame |
| NAV-04 Navigate from command in Techie | Cross-layout nav | From Techie Settings, click "Open Dashboard" (if such a button is added later) | `CurrentPageViewModel` becomes `DashboardViewModel`, no exception |
| NAV-05 Navigate from command in Minimal | Same | From Minimal Settings, click "Open Dashboard" | Same as above |
| NAV-06 Sidebar back-stack | Out of scope for v1 | n/a | Not implemented; document explicitly |

## G.6 Layout switching tests

| Test | Purpose | Scope | Failure indicators |
|------|---------|-------|---------------------|
| SW-01 Switch from Minimal to Techie | Basic switch | Call `SwitchLayout("Techie")` on a `LayoutHostViewModel` whose `ActiveLayout` is `MinimalLayoutView` | (a) `ActiveLayout` becomes `TechieLayoutView`, (b) `LayoutChanged` fires once, (c) Old `MinimalLayoutView` instance is eligible for GC |
| SW-02 Switch from Techie to Minimal | Reverse | Call `SwitchLayout("Minimal")` on a `LayoutHostViewModel` whose `ActiveLayout` is `TechieLayoutView` | (a) `ActiveLayout` becomes `MinimalLayoutView`, (b) `LayoutChanged` fires once, (c) Old `TechieLayoutView` instance is eligible for GC |
| SW-03 Switch to same name | Idempotence | Call `SwitchLayout("Minimal")` on a host already showing Minimal | (a) `LayoutChanged` does NOT fire, (b) `store.Save` is NOT called, (c) The view is not replaced |
| SW-04 Switch to invalid name | Validation | Call `SwitchLayout("Garbage")` | Throws `ArgumentException`, no state change, no event, no save |
| SW-05 Re-entrant switch | Guard | In the `LayoutChanged` handler, call `SwitchLayout` again | (a) The inner call is ignored (or queued, per the chosen implementation), (b) The system does not enter infinite recursion, (c) Final state is consistent |
| SW-06 Switch while a page is loading | Concurrency | Begin a page-load, then call `SwitchLayout` before the load completes | (a) The page-load completes against the new layout's data context, (b) No `ObjectDisposedException`, (c) The new layout's data context is the `DashboardViewModel` from `MainViewModel` |
| SW-07 Persist before/after switch | Ordering | Call `SwitchLayout("Techie")` and observe store writes | (a) The store write happens AFTER the visual swap, (b) If the swap throws, the store write does NOT happen (verify by injecting a fault into `ActiveLayout` setter) |
| SW-08 Settings panel triggers switch | UX flow | Open Settings, select Techie, click Apply | (a) Switch happens, (b) Persistence happens, (c) `LayoutChanged` fires, (d) Settings view does not crash and is not destroyed (it should remain on screen, just behind the new chrome) |

## G.7 Regression tests

| Test | Purpose | Scope | Failure indicators |
|------|---------|-------|---------------------|
| REG-01 Engine toggle still works | Backend regression | Click engine button in Minimal and Techie | Wallpaper starts/stops |
| REG-02 Telemetry still updates | Backend regression | Watch FPS, VRAM, RAM in both layouts | Values update every second |
| REG-03 Triple-click hero plays wallpaper | UX regression | Triple-click hero in both layouts | Wallpaper plays |
| REG-04 Updater banner appears | Backend regression | Trigger a fake update | Banner shows in both layouts with correct tokens |
| REG-05 Modal dialog appears | UX regression | Trigger install confirmation | Dialog overlays both layouts |
| REG-06 Window minimize stops preview | Platform regression | Minimize window in both layouts | Preview service stops, log line in `myeasylog.log` |
| REG-07 Window deactivate stops preview | Platform regression | Alt-Tab away in both layouts | Preview service stops |
| REG-08 Icon crop still works | Platform regression | Inspect taskbar icon in both layouts | Icon is the cropped, padded variant |
| REG-09 Mica backdrop applied | Platform regression | Inspect window in both layouts on Win 11 | Mica is visible (DWMWA_SYSTEMBACKDROP_TYPE=2) |
| REG-10 Immersive dark mode | Platform regression | Inspect titlebar in both layouts on Win 11 | Titlebar is dark |
| REG-11 Existing 27/27 unit tests pass | Whole-repo regression | Run `dotnet test` | All existing tests still pass |
| REG-12 Solution builds with 0 warnings, 0 errors | Build hygiene | Run `dotnet build -c Release` | 0 warnings, 0 errors (matching pre-migration baseline) |

---

# Phase H — Rollback Strategy

## H.1 General principles

1. **Tag every step.** Before each step's commit, create `pre-stepN-rollback` on the branch tip. The tag is the recovery point.
2. **No half-states land.** A step's commit is either the whole step or it doesn't land. Partial states live on a working branch or in a stash, never on the feature branch.
3. **Build must be green at every step boundary.** A step is not "done" until `dotnet build -c Release` returns 0 warnings, 0 errors AND the test suite is green.
4. **The blast radius is in Step 3.** All other steps are additive (new files + small modifications). Step 3 deletes files (`Views/*.xaml*`, `Theme/NeonTechStyle.xaml`) and reduces `MainWindow.xaml`. The rollback for Step 3 is `git revert <sha>` and the previous state is restored from the `pre-step3-rollback` tag.
5. **Independent safety net (v2):** `MainShellViewModel` is NOT introduced. The original `MainViewModel` is untouched through Step 3, and `LayoutHost` is exposed as a sub-property of `MainViewModel` (resolved lazily from DI on first access). The two-tier feature flag (T-3: `WT_DISABLE_TECHIE_LAYOUT` env var + `LocalSettings.techieDisabled` toggle) is the runtime safety net; the per-step `pre-stepN-rollback` tags are the source-control safety net.

## H.2 Per-phase rollback

| Phase | Rollback point | Rollback procedure | Success criteria |
|-------|----------------|--------------------|------------------|
| Step 0 | n/a (docs only) | `git rm` the three new docs. | Build is green. App boots unchanged. |
| Step 1 | `pre-step1-rollback` tag | `git revert <step1-commit-sha>` (or `git reset --hard pre-step1-rollback` if Step 1 is the latest commit). | **v3 Architecture Review Update:** the four `.gitkeep` folders (`Views/Minimal/`, `Views/Techie/`, `Views/Pages/`, `Views/Widgets/`) and `_LAYOUT_CONTRACT.md` are removed. The D-1 Part A `IDisposable` opt-in on `MainViewModel` and the `App.OnExit` explicit enumerated disposal list are reverted. The new `MainViewModelDisposalTests.cs` is removed. **No** `LayoutHostView.*` or `LayoutHostViewModel.cs` files were created in Step 1 (placeholder removed by v3 Architecture Review Update). Build is green. App boots unchanged. |
| Step 2 | `pre-step2-rollback` tag | `git revert <step2-commit-sha>`. Re-adds `d184105_*.xaml*` in root if not already present. | Build is green. The `Views/Techie/` tree exists (new files) but is not referenced at runtime. App boots unchanged into Minimal. The pre-`0ac1ed0` source is back in root for re-extraction if needed. |
| Step 3 | `pre-step3-rollback` tag | **`git revert <step3-commit-sha>` is the canonical rollback.** Restores: `MainWindow.xaml` (407 lines), `MainWindow.xaml.cs` (185 lines), the old `Views/*.xaml*` files (Dashboard, Library, Settings) at the OLD paths, `Theme/NeonTechStyle.xaml` at the old path, `App.xaml` with the old `Source`. Removes: `Views/Pages/*`, `Views/Minimal/*`, `Views/LayoutHostView.*`, `Views/LayoutHostViewModel.cs` (Step 3A). **v3 Architecture Review Update:** the D-1 Part A `IDisposable` opt-in on `MainViewModel` and the explicit enumerated disposal list in `App.OnExit` were added in Step 1 Scaffolding and remain in place after this revert (D-1 is not undone). No `App.DisposeServices()` method to remove. **v3:** No wrapper VM to remove (wrapper was never introduced). | Build is green. App boots with the pre-migration visual exactly. Manual test checklist (Phase E.5) all 12 items pass. D-1 Part A disposal test still passes. |
| Step 4 | `pre-step4-rollback` tag | `git revert <step4-commit-sha>`. Removes `Views/LayoutHostView.xaml(.cs)`, `Views/LayoutHostViewModel.cs` (the real impl, not the Step-1 placeholder). The `MainWindow` still hosts `LayoutHostView` per Step 3, but `LayoutHostViewModel` falls back to a no-op. | Build is green. App boots into Minimal (Step 3 behaviour). The `SwitchLayout` command exists but is a no-op until Step 6 wires persistence. |
| Step 5 | `pre-step5-rollback` tag | `git revert <step5-commit-sha>`. Removes: `Theme/Techie/` integration into `App.xaml`, the SubHero1/2/3 properties on `DashboardViewModel`, any Techie chrome / dashboard refactors. | Build is green. App boots into Minimal. Switching to Techie still triggers Step 4 behaviour (view loads with Minimal tokens — known visual mismatch). |
| Step 6 | `pre-step6-rollback` tag | `git revert <step6-commit-sha>`. Removes: `Core/Settings/ILayoutPreferenceStore.cs`, `Core/Settings/JsonLayoutPreferenceStore.cs`, `Core/Settings/LayoutPreference.cs`, the Settings view layout section in both layouts, the persistence wiring in `LayoutHostViewModel`. | Build is green. App boots into the layout that was active at the time of the tag. Settings still works but has no Layout section. No `layout.json` is read or written. |
| Step 7 | `pre-step7-rollback` tag | `git revert <step7-commit-sha>`. Removes the new test files. | Existing 27/27 tests pass. The new tests are gone. The migration feature itself is unaffected (it's a doc + tests-only change). |

## H.3 Emergency rollback (any step, no commit known)

If for any reason the pre-rollback tag is missing or invalid:

1. `git log --oneline -n 30` — find the last commit before the step started.
2. `git diff <last-good-sha>..HEAD` — verify the blast radius matches expectations.
3. `git reset --hard <last-good-sha>` — restore. The working tree returns to the last good state.
4. Re-create the pre-rollback tag: `git tag pre-emergency-rollback <last-good-sha>`.
5. Investigate, fix, re-attempt the step.

## H.4 Cross-step regressions

If a regression is discovered in Step N that was actually introduced in Step M (M < N):

1. The regression is in Step M's commit, not Step N's.
2. The fix is in Step M's domain. Reverting Step N does not fix it.
3. Use `git bisect` between `pre-stepM-rollback` and HEAD to find the offending commit.
4. The fix is a follow-up commit on the feature branch, NOT a revert of Step M. The pre-migration test suite (27/27) is the source of truth for "Step M was green when it landed." If the new tests in Step 7 catch the regression, that is a healthy signal.

## H.5 Recovery from a corrupted `layout.json`

This is a runtime (not build-time) failure mode:

1. `JsonLayoutPreferenceStore.Load` catches `JsonException`, logs to `DiagnosticsService`, returns `new LayoutPreference { CurrentLayout = "Minimal" }`.
2. The user sees Minimal at next boot, with a `myeasylog.log` line: `[JsonLayoutPreferenceStore] layout.json corrupt, defaulting to Minimal.`
3. No data is lost. The user can re-apply their preferred layout via Settings.
4. This is verified by MT-08 in Phase G.

## H.6 Recovery from a half-rendered layout

If the user sees a half-rendered layout (e.g., Techie chrome but Minimal footer) — this is the symptom of a binding or DataTemplate resolution failure. Recovery:

1. The app does NOT crash (binding failures produce `Trace.WriteLine` warnings, not exceptions).
2. The user can open Settings → switch layout → Apply. This forces a fresh layout tree to be instantiated.
3. If the Settings view itself is the broken surface, the user can edit `layout.json` by hand (path: `%APPDATA%/WallpaperTurbo/layout.json`) and set `"CurrentLayout": "Minimal"`, then restart.

## H.7 Success criteria for "at no point did the project become unrecoverable"

- Every commit on the feature branch builds with 0 warnings, 0 errors.
- Every step has a pre-rollback tag.
- `git revert` of any single step's commit restores the previous green state.
- The full feature branch can be `git reset --hard pre-step1-rollback` to return to the pre-migration state with no data loss.
- The 27/27 existing tests pass at every step boundary (verified by CI on every commit).
- The new tests in Step 7 are additive: removing them does not affect the migration feature.

---

# Phase I — Final Execution Roadmap

A developer-ready checklist, organised by phase. Each item is a single, completable, verifiable action. The engineer executing this checklist needs no further planning.

## I.0 Pre-migration housekeeping (do before Step 0)

- [ ] Verify `dotnet --version` returns 8.0.421 or compatible.
- [ ] Verify `git status` is clean on `feature/app-updater`.
- [ ] Create branch `feature/dual-layout-migration` from `feature/app-updater`.
- [ ] Run `dotnet build -c Release` and confirm 0 warnings, 0 errors. Save the output to `docs/testing/PRE_MIGRATION_BUILD.txt`.
- [ ] Run `dotnet test` and confirm 27/27 (or current count) tests pass. Save the output to `docs/testing/PRE_MIGRATION_TESTS.txt`.
- [ ] Capture a screen recording of the current Minimal UI (Dashboard / Library / Settings) and attach to `docs/testing/PRE_MIGRATION_SCREENCAST.md` as a reference for "no visual regression."
- [ ] Capture the working-set memory of the running app (Task Manager → Details → right-click → "Save snapshot") to `docs/testing/PRE_MIGRATION_MEMORY.txt`.
- [ ] Verify the file `d184105_DashboardView.xaml` in repo root is byte-equal to `git show 0ac1ed0^:src/WallpaperTurbo.UI/Views/DashboardView.xaml`. If not, STOP — the Techie source-of-truth is not what we think.
- [ ] Verify the file `d184105_DashboardView.xaml.cs` in repo root is byte-equal to `git show 0ac1ed0^:src/WallpaperTurbo.UI/Views/DashboardView.xaml.cs`. If not, STOP.
- [ ] Create tag `pre-migration-rollback` on the feature branch tip.

## I.1 Step 0 — Navigation Architecture Snapshot

- [ ] Create `docs/architecture/NAVIGATION_SNAPSHOT.md` listing all 8 sidebar entries with: VM, View, DataTemplate, accessibility name, deep-link (none), code-behind handlers (none for unwired).
- [ ] Create `docs/architecture/VM_CAPABILITY_MATRIX.md` with a row per VM and a column per observable property, command, and binding used in the current XAML.
- [ ] Create `docs/architecture/SHARED_BACKEND_INVARIANTS.md` listing: WallpaperService, TelemetryService, IWallpaperLibraryService, Updater stack, settings store, and confirming "all layouts must consume via this surface, no exceptions."
- [ ] Commit: `docs: add navigation, VM capability, and backend invariant snapshots (Step 0)`.
- [ ] Tag: `pre-step0-rollback`.
- [ ] **Validation:** PR review of the three docs by a senior engineer unfamiliar with the repo, completed in < 15 minutes.

## I.2 Step 1 — Scaffolding (v3 Architecture Review Update — scaffolding only)

> **Scope:** folder structure, `_LAYOUT_CONTRACT.md`, D-1 Part A leak fix on `MainViewModel`, explicit enumerated disposal in `App.OnExit`. **No `LayoutHostViewModel` is created in Step 1. No Minimal/Techie extraction begins in Step 1.**

- [ ] Create `src/WallpaperTurbo.UI/Views/_LAYOUT_CONTRACT.md` with the 6 rules from Phase B.1 (plus the v3 ownership note: `LayoutHostViewModel` is chrome/container only; navigation stays in `MainViewModel`).
- [ ] Create the four empty folders `src/WallpaperTurbo.UI/Views/Minimal/`, `Views/Techie/`, `Views/Pages/`, `Views/Widgets/`, each with a `.gitkeep` marker.
- [ ] ~~Create `src/WallpaperTurbo.UI/Views/LayoutHostView.xaml` — empty `<Grid>` with a placeholder `ContentControl`.~~ — **REMOVED in v3.** `LayoutHostView` is first introduced in **Step 3A** (real impl, not placeholder). See I.4.b.
- [ ] ~~Create `src/WallpaperTurbo.UI/Views/LayoutHostView.xaml.cs` — `InitializeComponent` only.~~ — **REMOVED in v3.** See I.4.b.
- [ ] ~~Create `src/WallpaperTurbo.UI/Views/LayoutHostViewModel.cs` — `CurrentLayoutName` property, `SwitchLayoutCommand`.~~ — **REMOVED in v3.** `LayoutHostViewModel` is first introduced in **Step 3A**. See I.4.b.
- [ ] ~~Create `src/WallpaperTurbo.UI/Views/Minimal/MinimalLayoutView.xaml(.cs)` — placeholder.~~ — **REMOVED in v3.** `MinimalLayoutView` lands in **Step 3A**. See I.4.b.
- [ ] ~~Create `src/WallpaperTurbo.UI/Views/Minimal/MinimalLayoutViewModel.cs` — placeholder.~~ — **REMOVED in v3.** See I.4.b.
- [ ] ~~Create `src/WallpaperTurbo.UI/Views/Techie/TechieLayoutView.xaml(.cs)` — placeholder.~~ — **REMOVED in v3.** `TechieLayoutView` lands in **Step 2A** (Techie extraction). See I.3.
- [ ] ~~Create `src/WallpaperTurbo.UI/Views/Techie/TechieLayoutViewModel.cs` — placeholder.~~ — **REMOVED in v3.** See I.3.
- [ ] ~~Modify `src/WallpaperTurbo.UI/App.xaml.cs` — add 6 singleton registrations in `ConfigureServices()`...~~ — **REMOVED in v3.** No layout DI registrations in Step 1. The 6 registrations land in Step 2A and Step 3A.
- [ ] Modify `src/WallpaperTurbo.UI/ViewModels/MainViewModel.cs` — add `IDisposable` implementation; `Dispose()` unsubscribes from `_telemetryService.MetricsUpdated`. **(D-1 Part A, approved.)**
- [ ] Modify `src/WallpaperTurbo.UI/App.xaml.cs` — extend `OnExit`'s explicit enumerated disposal list to include `MainViewModel` (after the existing `UpdateCoordinator` block, in registration/declaration order). Each new component wrapped in a `try/catch`. **Do NOT dispose `IServiceProvider`.** No `App.DisposeServices()` method.
- [ ] Create `tests/WallpaperTurbo.Tests/Layout/MainViewModelDisposalTests.cs` — D-1 Part A test: `MainViewModel_Dispose_UnhooksTelemetry` passes.
- [ ] Build: 0 warnings, 0 errors.
- [ ] Run app, confirm MainWindow opens and Minimal dashboard renders.
- [ ] Commit: `feat(layouts): scaffold view folders, D-1 Part A leak fix, OnExit enumeration (Step 1)`.
- [ ] Tag: `pre-step1-rollback`.
- [ ] **Validation:** no behavioural change; build green; disposal test green; explicit enumeration does not throw on exit.

## I.3 Step 2 — Extract Techie from d184105

- [ ] (2.1) `git show 0ac1ed0^:src/WallpaperTurbo.UI/Theme/NeonTechStyle.xaml` — read the pre-`0ac1ed0` theme into memory.
- [ ] (2.2) Create `src/WallpaperTurbo.UI/Theme/Techie/NeonTechStyle.xaml` — verbatim copy of the pre-`0ac1ed0` file.
- [ ] (2.2) Create `src/WallpaperTurbo.UI/Theme/Techie/_THEME_KEYS.md` — list of every `x:Key` in the Techie theme.
- [ ] (2.3) Create `src/WallpaperTurbo.UI/Views/Techie/TechieDashboardView.xaml` — verbatim copy of `d184105_DashboardView.xaml` with only `x:Class` and `xmlns:` edits.
- [ ] (2.3) Create `src/WallpaperTurbo.UI/Views/Techie/TechieDashboardView.xaml.cs` — verbatim copy of `d184105_DashboardView.xaml.cs` with only `namespace` edits.
- [ ] (2.4) From `git show 0ac1ed0^:src/WallpaperTurbo.UI/MainWindow.xaml`, extract: sidebar `<Border Grid.Column="0">…` block, titlebar `<Grid Grid.Row="0">…` block, footer `<Border Grid.Row="3">…` block, modal overlay `<Grid Grid.Column="0" Grid.ColumnSpan="2" Panel.ZIndex="1000">…` block, updater banner `<Border Grid.Row="1">…` block.
- [ ] (2.4) Create `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieSidebarView.xaml(.cs)` — extracted sidebar.
- [ ] (2.4) Create `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieTitleBarView.xaml(.cs)` — extracted titlebar.
- [ ] (2.4) Create `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieFooterView.xaml(.cs)` — extracted footer.
- [ ] (2.4) **v2:** Skip the Techie modal overlay extraction. Modal stays in `MainWindow.xaml` (per Step 0.5 invariant); layouts do not host it.
- [ ] (2.4) Create `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieUpdaterBanner.xaml(.cs)` — extracted updater banner.
- [ ] (2.5) Delete `d184105_DashboardView.xaml` and `d184105_DashboardView.xaml.cs` from the repo root.
- [ ] (2.5) `git diff` confirms the only differences from the pre-`0ac1ed0` source are namespace renames.
- [ ] Build: 0 warnings, 0 errors.
- [ ] Commit: `feat(techie): extract pre-0ac1ed0 sources to Views/Techie (Step 2)`.
- [ ] Tag: `pre-step2-rollback`.
- [ ] **Validation:** solution builds; new files compile independently; nothing references the new types at runtime.

## I.4 Step 3 — Move current UI into MinimalLayoutView

**This is the highest-risk step. Split into 3 sub-steps if needed.**

> v2 note: `MainShellViewModel` is NOT introduced. `MainWindow.DataContext` remains `MainViewModel`; `LayoutHost` is exposed as a sub-property of `MainViewModel` and is resolved lazily from DI on first access (Step 3.5 contract). This removes the v1 sub-step I.4.a entirely.

### I.4.a Reduce MainWindow to host
- [ ] Modify `src/WallpaperTurbo.UI/MainWindow.xaml` — delete Rows 1 and 2's content (sidebar and right pane). Replace with `<ContentControl Content="{Binding LayoutHost}"/>`.
- [ ] Move modal overlay from inside the deleted grid into a new grid that contains both the `ContentControl` and the overlay (modal stays in `MainWindow.xaml`; per Step 0.5 invariant).
- [ ] Keep the modal overlay at `Panel.ZIndex="1000"`.
- [ ] **v2:** `MainWindow.xaml.cs.DataContext` is UNCHANGED — it remains `MainViewModel` (no wrapper swap). `LayoutHost` is a sub-property of `MainViewModel` and resolves on first access.
- [ ] Modify `src/WallpaperTurbo.UI/App.xaml` — change `Source` to `Theme/Minimal/NeonTechStyle.xaml` after that folder is created in I.4.b. For now, keep the old path.
- [ ] At this point, the app will not render correctly because the chrome is gone. STOP and proceed immediately to I.4.b. Do not commit a broken state.

### I.4.b Move Minimal chrome and views (v2: uses `Views/Minimal/`, `Views/Pages/`; no `MainShellViewModel`)
- [ ] Create `src/WallpaperTurbo.UI/Theme/Minimal/NeonTechStyle.xaml` — copy of the current `Theme/NeonTechStyle.xaml`.
- [ ] Delete `src/WallpaperTurbo.UI/Theme/NeonTechStyle.xaml`.
- [ ] Modify `src/WallpaperTurbo.UI/App.xaml` — change `Source` to `Theme/Minimal/NeonTechStyle.xaml` (kept here, NOT moved into `MinimalLayoutView.Resources`, per Step 5 conservatism — the `App.xaml` merge is the single place all layouts inherit Minimal tokens).
- [ ] Create `src/WallpaperTurbo.UI/Views/Minimal/Chrome/MinimalSidebarView.xaml(.cs)` — extracted from `MainWindow.xaml`.
- [ ] Create `src/WallpaperTurbo.UI/Views/Minimal/Chrome/MinimalTitleBarView.xaml(.cs)` — extracted from `MainWindow.xaml` (no WPF-UI `<ui:TitleBar>`; that's `MainWindow`'s job per T-1).
- [ ] Create `src/WallpaperTurbo.UI/Views/Minimal/Chrome/MinimalUpdaterBanner.xaml(.cs)` — extracted from `MainWindow.xaml`.
- [ ] Create `src/WallpaperTurbo.UI/Views/Pages/DashboardView.xaml(.cs)` — verbatim copy of old `Views/DashboardView.xaml(.cs)` with namespace edits to `Views.Pages`. (Shared page; v2 does NOT create `Minimal/MinimalDashboardView.xaml`.)
- [ ] Create `src/WallpaperTurbo.UI/Views/Pages/LibraryView.xaml(.cs)` — verbatim copy of old `Views/LibraryView.xaml(.cs)` with namespace edits to `Views.Pages`.
- [ ] Create `src/WallpaperTurbo.UI/Views/Pages/SettingsView.xaml(.cs)` — verbatim copy of old `Views/SettingsView.xaml(.cs)` with namespace edits to `Views.Pages`. (Shared by BOTH layouts per Step 2.5; v2 does NOT create `Minimal/MinimalSettingsView.xaml`.)
- [ ] Create `src/WallpaperTurbo.UI/Views/Minimal/MinimalLayoutViewModel.cs` — `CurrentPageViewModel` (default `_dashboardViewModel`) and `NavigateCommand` (lifted from `MainViewModel.NavigateCommand`).
- [ ] Create `src/WallpaperTurbo.UI/Views/Minimal/MinimalLayoutView.xaml` — composes the chrome + `<ContentPresenter Content="{Binding CurrentPageViewModel}"/>` + `Resources.MergedDictionaries` (Minimal tokens only, NOT App.xaml tokens) + `DataTemplate DataType` mappings for `DashboardViewModel` → `Pages/DashboardView`, `LibraryViewModel` → `Pages/LibraryView`, `SettingsViewModel` → `Pages/SettingsView`.
- [ ] Create `src/WallpaperTurbo.UI/Views/Minimal/MinimalLayoutView.xaml.cs` — `InitializeComponent`, sets `DataContext = MinimalLayoutViewModel`.
- [ ] Delete the OLD `src/WallpaperTurbo.UI/Views/DashboardView.xaml(.cs)`, `LibraryView.xaml(.cs)`, `SettingsView.xaml(.cs)` (replaced by `Views/Pages/`).
- [ ] **v2:** Do NOT delete the `Views/` directory — it now contains `Pages/`, `Minimal/`, `Techie/`, `Widgets/`, `LayoutHostView.xaml(.cs)`, and `_LAYOUT_CONTRACT.md`.
- [ ] Modify `src/WallpaperTurbo.UI/App.xaml.cs` — register `LayoutHostViewModel`, `LayoutHostView`, `MinimalLayoutViewModel`, `MinimalLayoutView`, `MainViewModel` (unchanged) as singletons. **v2:** register `FeatureFlagService` (T-3).
- [ ] Modify `src/WallpaperTurbo.UI/ViewModels/MainViewModel.cs` — add `public LayoutHostViewModel LayoutHost { get; }` (resolved from DI in the constructor; never null after construction). Expose `ICommand SwitchToTechieCommand` and `ICommand SwitchToMinimalCommand` that delegate to `LayoutHost.SwitchLayout(...)`.
- [ ] Build: 0 warnings, 0 errors.
- [ ] Run the 12-item regression checklist (Phase E.5).
- [ ] Commit: `feat(minimal): move current UI into MinimalLayoutView (Step 3)`.
- [ ] Tag: `pre-step3-rollback`.
- [ ] **Validation:** visual is pixel-identical to pre-Step-3. All 12 regression items pass.

## I.5 Step 4 — LayoutHost Integration

- [ ] Modify `src/WallpaperTurbo.UI/Views/LayoutHostView.xaml` — replace the placeholder `ContentControl` with `<ContentControl Content="{Binding ActiveLayout}"/>`.
- [ ] Modify `src/WallpaperTurbo.UI/Views/LayoutHostView.xaml.Resources` — add the two `DataTemplate DataType` mappings for `MinimalLayoutViewModel` → `MinimalLayoutView` and `TechieLayoutViewModel` → `TechieLayoutView`. Import `xmlns:minimal` and `xmlns:techie`.
- [ ] Modify `src/WallpaperTurbo.UI/Views/LayoutHostView.xaml.cs` — set `DataContext = LayoutHostViewModel` in the constructor (resolve from DI).
- [ ] Modify `src/WallpaperTurbo.UI/Views/LayoutHostViewModel.cs` — constructor resolves `IServiceProvider` and the two layout VMs. `ActiveLayout` returns the VM corresponding to `CurrentLayoutName`. `SwitchLayout(string name)` updates the name and re-evaluates `ActiveLayout`.
- [ ] Modify `src/WallpaperTurbo.UI/Views/_LAYOUT_CONTRACT.md` — document the DataTemplate scoping rule.
- [ ] Modify `src/WallpaperTurbo.UI/App.xaml.cs` — register `LayoutHostView` and `LayoutHostViewModel` as singletons (the explicit enumerated disposal list from Step 1 is preserved; the `LayoutHostViewModel` entry is added to the list at this step, gated on the Step 3A `LAYOUT_HOST_LIFECYCLE.md` documentation per the v3 Architecture Review Update).
- [ ] Build: 0 warnings, 0 errors.
- [ ] Run app, confirm Minimal is the default. Confirm switching to Techie in a debug menu shows the TechieLayoutView (with the known visual mismatch — that's Step 5's job).
- [ ] Run MEM-02 (100 switches) — managed heap does not grow by more than 2 MB.
- [ ] Commit: `feat(layouts): LayoutHost resolves active layout by name (Step 4)`.
- [ ] Tag: `pre-step4-rollback`.
- [ ] **Validation:** Minimal default, Techie switch works (visual mismatch acceptable), memory stable.

## I.6 Step 5 — Refactor Techie hotspots

> v2 conservatism: do NOT refactor `DropShadowEffect`, telemetry pulses, or inertia scroll. The `WM_MOUSEHWHEEL` refactor is gated on MEM-02 profiling evidence.

- [ ] (5a) **v2 deferral (T-9):** `DashboardViewModel.cs` already exposes `SubHero1/2/3` (lines 68-70); no VM change is required. Defer adding `SubHero1/2/3` XAML elements to a 1.4.0 ticket. The dead code-behind references in `Views/Techie/Chrome/TechieDashboardView.xaml.cs` (lines 240-242) STAY — they compile and are harmless.
- [ ] (5a) ~~Build, commit: `feat(vm): add RecentlyUsedPreview1-3 view-state to DashboardViewModel (Step 5a)`.~~ — **DROPPED in v2** (no VM change needed).
- [ ] (5b) Modify `src/WallpaperTurbo.UI/Theme/Techie/NeonTechStyle.xaml` — no content change. This step verifies it.
- [ ] (5b) Modify `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieSidebarView.xaml` — verify every binding resolves. Adjust namespaces only. Do NOT add new visual effects.
- [ ] (5b) Modify `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieTitleBarView.xaml` — same.
- [ ] (5b) Modify `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieFooterView.xaml` — same.
- [ ] (5b) Modify `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieUpdaterBanner.xaml` — same.
- [ ] (5b) ~~Modify `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieModalOverlayView.xaml`~~ — **DROPPED in v2** (no Techie modal; modal stays in `MainWindow.xaml`).
- [ ] (5b) Modify `src/WallpaperTurbo.UI/Views/Techie/TechieDashboardView.xaml` — verify bindings resolve. Adjust namespaces. Do NOT touch `DropShadowEffect`, telemetry pulses, or inertia scroll.
- [ ] (5b) Modify `src/WallpaperTurbo.UI/Views/Techie/Chrome/TechieDashboardView.xaml.cs` — verify `OnCardMouseLeftButtonDown` switch is intact. Adjust namespaces.
- [ ] (5b) Modify `src/WallpaperTurbo.UI/Views/Techie/TechieLibraryView.xaml(.cs)` — Techie tokens, namespace edits.
- [ ] (5b) ~~Modify `src/WallpaperTurbo.UI/Views/Techie/TechieSettingsView.xaml(.cs)`~~ — **DROPPED in v2** (no `TechieSettingsView`; both layouts use `Views/Pages/SettingsView` per Step 2.5).
- [ ] (5b) Modify `src/WallpaperTurbo.UI/Views/Techie/TechieLayoutView.xaml(.cs)` — composes chrome + ContentPresenter + `Resources.MergedDictionaries` includes `Theme/Techie/NeonTechStyle.xaml` (per-layout dictionary scope) + `DataTemplate DataType` mappings.
- [ ] (5b) Modify `src/WallpaperTurbo.UI/Views/Techie/TechieLayoutViewModel.cs` — `CurrentPageViewModel` and `NavigateCommand`.
- [ ] (5b) Modify `src/WallpaperTurbo.UI/Views/Minimal/MinimalLayoutView.xaml` — `Resources.MergedDictionaries` includes `Theme/Minimal/NeonTechStyle.xaml` (per-layout dictionary scope; symlink/copy of pre-`0ac1ed0` Step 3 file).
- [ ] (5b) Modify `src/WallpaperTurbo.UI/App.xaml` — REMOVE the `Theme/Minimal/NeonTechStyle.xaml` reference. (Both layouts now bring their own theme via `LayoutView.Resources.MergedDictionaries`.) Only WPF-UI `ThemesDictionary` + `ControlsDictionary` remain in `App.xaml`.
- [ ] (5b) Create `src/WallpaperTurbo.UI/Views/Techie/_TECHE_HOTSPOTS.md` — document every adjustment (incl. v2 conservatism: DropShadowEffect, telemetry pulses, inertia scroll left UNTOUCHED).
- [ ] (5b) Run the theme-isolation grep tests: `git grep "Views.Techie" src/WallpaperTurbo.UI/Views/Minimal/` returns nothing. `git grep "Views.Minimal" src/WallpaperTurbo.UI/Views/Techie/` returns nothing. `git grep "Theme/Techie" src/WallpaperTurbo.UI/Views/Minimal/` returns nothing. `git grep "Theme/Minimal" src/WallpaperTurbo.UI/Views/Techie/` returns nothing.
- [ ] (5b) Build: 0 warnings, 0 errors.
- [ ] (5b) Commit: `feat(techie): refactor chrome and dashboard for current VM contract (Step 5)`.
- [ ] Tag: `pre-step5-rollback`.
- [ ] **Validation:** Techie renders correctly. Minimal unchanged. MT-04, MT-05, MT-06, MT-15 pass. MEM-02 baseline (100 switches) shows no managed-heap growth > 2MB; if it does, log to `docs/architecture/MEM-02_BASELINE.md` and defer the `WM_MOUSEHWHEEL` refactor.

## I.7 Step 6 — Settings + Layout Switching

> v2: shared `Pages/SettingsView` (no `MinimalSettingsView` / `TechieSettingsView`); `MainViewModel.LayoutHost` is the single source-of-truth for layout switching; `MainShellViewModel` is NOT introduced.

- [ ] (6a) Create `src/WallpaperTurbo.Core/Settings/LayoutPreference.cs` — record with `string CurrentLayout` (validation in static factory or property setter).
- [ ] (6a) Create `src/WallpaperTurbo.Core/Settings/ILayoutPreferenceStore.cs` — `LayoutPreference Load()`, `void Save(LayoutPreference)`.
- [ ] (6a) Create `src/WallpaperTurbo.Core/Settings/JsonLayoutPreferenceStore.cs` — file-based impl, atomic write (T-4: temp file + rename), try/catch around IO, logs to `DiagnosticsService`.
- [ ] (6a) Modify `src/WallpaperTurbo.UI/App.xaml.cs` — register `ILayoutPreferenceStore` as singleton.
- [ ] (6a) Build, commit: `feat(settings): add ILayoutPreferenceStore (Step 6a)`.
- [ ] (6b) Modify `src/WallpaperTurbo.UI/Views/LayoutHostViewModel.cs` — read `ILayoutPreferenceStore` in constructor, populate `CurrentLayoutName` from it, expose `LayoutChanged` event, persist in `SwitchLayout` (T-4: apply visually FIRST, persist LAST), add `_switchInProgress` re-entrancy guard (T-6), catch exceptions and increment `LayoutSwitchErrorCount` (T-7), measure `LayoutSwitchDurationMs` (T-7). Honour `FeatureFlagService.IsTechieEnabled` (T-3) — calls with `name = "Techie"` are no-ops when the flag is off.
- [ ] (6b) Build, commit: `feat(layouts): persist layout preference (Step 6b)`.
- [ ] (6c) Modify `src/WallpaperTurbo.UI/Views/Pages/SettingsView.xaml` — add a "Layout" section with two `RadioButton`s (`Minimal`, `Techie`) and an "Apply" button. (Both layouts use this view per Step 2.5.)
- [ ] (6c) Modify `src/WallpaperTurbo.UI/Views/Pages/SettingsView.xaml.cs` (or its associated `SettingsViewModel`) — add `ApplyLayoutCommand` that calls `MainViewModel.LayoutHost.SwitchLayout`.
- [ ] (6c) Modify `src/WallpaperTurbo.UI/Views/Pages/SettingsView.xaml` Reset button — also reset layout to Minimal.
- [ ] (6c) Modify `src/WallpaperTurbo.UI/Views/_LAYOUT_CONTRACT.md` (or its successor) — document the preference schema and the migration story (existing users default to Minimal).
- [ ] (6c) Modify `src/WallpaperTurbo.UI/App.xaml.cs` — ensure `App.OnStartup` resolves `MainViewModel` (and transitively `LayoutHostViewModel` via `LayoutHost` sub-property, `MainWindow`) before `MainWindow.Show()` so the persisted layout is active on the first frame. Honour `WT_DISABLE_TECHIE_LAYOUT` env var: if set, clear any persisted Techie preference to Minimal on startup (T-3).
- [ ] (6c) Add `docs/architecture/ROLLBACK.md` — documents the T-8 rollback drill (env var + per-step tags + toggling `LocalSettings.techieDisabled`).
- [ ] Build: 0 warnings, 0 errors.
- [ ] Run MT-01, MT-02, MT-03, MT-07, MT-08, MT-12, SW-07, SW-08.
- [ ] Run MEM-02 (100 switches) — log result to `docs/architecture/MEM-02_BASELINE.md`.
- [ ] Commit: `feat(settings): layout picker in settings view (Step 6)`.
- [ ] Tag: `pre-step6-rollback`.
- [ ] **Validation:** persistence round-trip works. Corrupt file fallback works. Re-entrancy guard works. Reset works. Memory stable.

## I.8 Step 7 — Testing

- [ ] Create `tests/WallpaperTurbo.Tests/Layout/LayoutHostViewModelTests.cs` — 6 tests: same-name no-op, different-name fires once, re-entrancy guard, save called once, invalid name throws, idempotence under rapid calls.
- [ ] Create `tests/WallpaperTurbo.Tests/Layout/LayoutPreferenceStoreTests.cs` — 6 tests: missing file, valid file, corrupt file, atomic write, save failure does not throw, round-trip.
- [ ] Create `tests/WallpaperTurbo.Tests/Layout/NavigationContractTests.cs` — 4 tests: Dashboard nav, Library nav, Settings nav, unknown destination no-op.
- [ ] Create `tests/WallpaperTurbo.Tests/Layout/SubHeroPropertyTests.cs` — 3 tests: empty list, partial list, full list, INPC.
- [ ] Create `tests/WallpaperTurbo.Tests/Layout/ThemeIsolationTests.cs` — 4 grep-based tests.
- [ ] Create `tests/WallpaperTurbo.Tests/Layout/LayoutSwitchingIntegrationTest.cs` — 4 tests: switch forward, switch reverse, persist ordering, no leak.
- [ ] Create `tests/WallpaperTurbo.Tests/Layout/DataTemplateResolutionTest.cs` — 2 tests: Minimal scope, Techie scope.
- [ ] Create `tests/WallpaperTurbo.Tests/Layout/SettingsViewLayoutApplyTest.cs` — 2 tests: Apply Techie, Apply Minimal.
- [ ] Create `tests/WallpaperTurbo.Tests/Layout/MemoryTests.cs` — 5 tests: MEM-01 through MEM-05.
- [ ] Create `docs/testing/LAYOUT_MIGRATION_MANUAL_TESTS.md` — 17 manual test procedures (MT-01 through MT-17).
- [ ] Create `docs/testing/PERF_BASELINE.md` — capture pre-migration perf and post-migration perf side-by-side.
- [ ] Run `dotnet test` and confirm all tests pass.
- [ ] Run the 12-item regression checklist (Phase E.5).
- [ ] Commit: `test(layouts): add layout, navigation, persistence, memory tests (Step 7)`.
- [ ] Tag: `pre-step7-rollback`.
- [ ] **Validation:** 27/27 existing + ≥ 10 new = ≥ 37/37 tests pass. 0 warnings, 0 errors. All 12 regression items pass. All 17 manual tests pass.

## I.9 Post-migration

- [ ] Run `dotnet build -c Release` and confirm 0 warnings, 0 errors. Save the output to `docs/testing/POST_MIGRATION_BUILD.txt`.
- [ ] Run `dotnet test` and confirm ≥ 37/37 tests pass. Save to `docs/testing/POST_MIGRATION_TESTS.txt`.
- [ ] Capture a screen recording of both Minimal and Techie side-by-side. Attach to `docs/testing/POST_MIGRATION_SCREENCAST.md`.
- [ ] Capture the working-set memory. Compare to pre-migration. Document in `docs/testing/POST_MIGRATION_MEMORY.txt`.
- [ ] Open the PR from `feature/dual-layout-migration` to `feature/app-updater` (or `main`, per team policy).
- [ ] Request review from a senior WPF architect.
- [ ] Address review comments.
- [ ] Squash and merge.

---

## Open questions

1. **Solution file (resolved).** No `*.sln` or `*.slnx` is in the repo root. The project is built via `dotnet build` against the `.csproj` directly. **Decision:** do not proactively investigate or add one. If `dotnet test` or other tooling fails because of its absence, create one as a fix-forward action.
2. **Original `installer.iss` content.** It's currently 0 bytes. Out of scope for this plan, but the next installer build will fail until the team restores it. Worth flagging in a parallel ticket.
3. **`d184105_*.xaml*` file naming.** The files in the repo root are named after a different commit (`d184105` = the updater commit, not the pre-`0ac1ed0` commit). Looks like a copy-artifact naming error from a previous extraction. The plan treats them as pre-`0ac1ed0` source (which they are) and the rename in Step 2 makes the file names match the truth.
4. **`AGENTS.md` staleness.** The repo's `AGENTS.md` claims this is a "skills" repo. It's a desktop app. Out of scope for this migration, but a future ticket should rewrite it for the real project.
5. **Playlists wiring.** `MainViewModel.NavigateCommand` does not handle `Playlists`. If the user clicks "Playlists" in the sidebar, nothing happens. This is a pre-existing bug. The migration preserves it. Whether to wire it up in Step 6 or as a separate ticket is a question for the team.
6. **`Mica` and `DWM` behaviour on Win 10.** The platform code targets Win 11 (`Mica` is Win 11 22H2+). If the app must run on Win 10, the `Mica` call returns silently and the window uses the system default. Not a regression, but worth knowing.
7. **Layout picker UX.** Should it be a single button "Switch layout" that toggles, or a radio with Apply? The plan picks radio + Apply (no live preview) for transactional behaviour. The team may want a toggle for snappier UX — out of scope here, but flag it.

## Per-finding severity table (v2 validation)

| Finding | Severity | Status in v2 | Step where addressed | Residual risk |
|---------|----------|--------------|----------------------|---------------|
| **D-1** `App._serviceProvider` is never disposed; only `UpdateCoordinator` is disposed in `OnExit` | CRITICAL | Addressed (D-1 fix, **v3 Architecture Review Update: split into Part A + Part B**) | Step 1 Scaffolding (Part A) / Step 1 Scaffolding (Part B planning) / Step 3A (LayoutHostViewModel `IDisposable`) | Low — Part A: `MainViewModel` implements `IDisposable`; `Dispose()` unsubscribes from `_telemetryService.MetricsUpdated`. Approved for implementation, lands in Step 1 Scaffolding. Part B: `TelemetryService` `IDisposable` review (T-12) is planning only; lifecycle ownership documented in `docs/architecture/TELEMETRY_SERVICE_LIFECYCLE.md` before any implementation. `App.OnExit` uses an **explicit enumerated list** of known `IDisposable` components (NOT `DisposeServices()`, NOT `IServiceProvider.Dispose()`) — `UpdateCoordinator` → `MainViewModel` → `TelemetryService` (if Part B approves) → future `LayoutHostViewModel` (Step 3A, documented first). |
| **M-1** Layout-switch interaction with modal overlay, `OnExit`, `OnClosing` not specified | HIGH | Addressed (M-1 fix) | Step 0.5, Step 3.5 | Medium — depends on Step 0.5 empirical test confirming the WPF-UI `TitleBar` placement |
| **HIGH-1** v1 plan uses `Layouts/` paths; v2 uses `Views/Minimal/`, `Views/Techie/`, `Views/Pages/`, `Views/Widgets/` | HIGH | Addressed (HIGH-1 fix) | All Steps | Low — every `Layouts/` reference replaced with `Views/` subtree path |
| **HIGH-2** v1's `MainShellViewModel` wrapper is unnecessary indirection; v2 drops it | HIGH | Addressed (HIGH-2 fix) | Step 3, Step 4 | Low — `MainViewModel.LayoutHost` is the simpler integration point |
| **MEDIUM-1** `MainShellViewModel` wrapper recommended to drop; `MainViewModel.LayoutHost` as sub-property | MEDIUM | Addressed (MEDIUM-1 fix) | Step 3, Step 4 | Low — sub-property pattern is straightforward |
| **MEDIUM-2** Step 0/Step 1/Step 3 need feature flag + disposal + D-1 | MEDIUM | Addressed (MEDIUM-2 fix, **v3 Architecture Review Update: explicit enumeration replaces `DisposeServices()`**) | Step 0, Step 0.5, Step 1 Scaffolding | Low — `FeatureFlagService` and the explicit enumerated disposal list in `App.OnExit` (NOT `App.DisposeServices()`, NOT `IServiceProvider.Dispose()`) are the mitigation. D-1 is split into Part A (approved, Step 1) and Part B (planning, T-12). |
| **MEDIUM-3** Modal overlay in `MainWindow.xaml` must not move to layout view | MEDIUM | Addressed (MEDIUM-3 fix) | Step 3, Step 0.5 | Low — invariant stated, enforced by code review |
| **MEDIUM-4** `DataTemplate DataType` XAML contract in `LayoutHostView.Resources` | MEDIUM | Addressed (MEDIUM-4 fix) | Step 3.5 | Low — XAML contract sketch is ≤10 lines, with a test in Phase G |
| **MEDIUM-5** Techie chrome (DropShadowEffect, telemetry pulses, inertia scroll) — refactor or leave? | MEDIUM | Addressed (MEDIUM-5 fix) | Step 5 conservatism: do NOT refactor | Low — `WM_MOUSEHWHEEL` refactor gated on MEM-02 baseline |
| **MEDIUM-6** Step 5 lists `TechieSettingsView.xaml` — wrong; should use shared `Pages/SettingsView` | MEDIUM | Addressed (MEDIUM-6 fix) | Step 2.5, Step 6 | Low — Step 2.5 partition table documents the shared settings decision |
| **T-1** WPF-UI `<ui:TitleBar>` placement in `MainWindow` vs `LayoutView` | TICKET | Addressed (T-1) | Step 0.5 | Low — empirical test in Step 0.5; default assumption is "stays in MainWindow" |
| **T-2** WPF SDK globbing — does the csproj need `<Compile>`/`<Page>` overrides? | TICKET | Addressed (T-2) | Step 1 | None — verified that WPF SDK auto-picks up new `.xaml`/`.xaml.cs` under the project |
| **T-3** `LocalSettings.techieDisabled` feature flag + `WT_DISABLE_TECHIE_LAYOUT` env var | TICKET | Addressed (T-3) | Step 0, Step 6 | Low — `FeatureFlagService` and `LayoutHostViewModel` honour the flag |
| **T-4** `SwitchLayout` atomic write (apply visually first, persist last) | TICKET | Addressed (T-4) | Step 6 | Low — temp file + rename, plus try/catch around IO |
| **T-5** TitleBar placement verification | TICKET | Addressed (T-5) | Step 0.5 | None — covered by T-1 |
| **T-6** `SwitchLayout` re-entrancy guard | TICKET | Addressed (T-6) | Step 6 | Low — `_switchInProgress` boolean; test in `LayoutHostReentrancyTests` |
| **T-7** Layout-switch health metrics (duration, error count) | TICKET | Addressed (T-7) | Step 6 | Low — `LayoutSwitchDurationMs` and `LayoutSwitchErrorCount` on `TelemetryService` |
| **T-8** Rollback drill documented in `docs/architecture/ROLLBACK.md` | TICKET | Addressed (T-8) | Step 6 | None — drill is part of the Step 6 checklist |
| **T-9** SubHero1/2/3 — properties exist in `DashboardViewModel` (lines 68-70) but no XAML elements use them | TICKET | Addressed (T-9) | Step 5 deferral | Low — defer to 1.4.0 ticket; dead code-behind references compile and are harmless |
| **T-10** Version bump 1.2.2 vs 1.3.0 | TICKET | Addressed (T-10) | Step 7 | None — team decides; this is a packaging concern, not an architecture concern |
| **MEM-01** Layout switching mechanism — option (a) `DataTemplate DataType` in `LayoutHostView.Resources` | MEMO | Addressed (MEM-01) | Step 3.5 | Low — XAML contract sketch + Phase G test |
| **MEM-02** Profile 100 layout switches for managed-heap growth | MEMO | Addressed (MEM-02) | Step 5, Step 6 | Low — baseline logged to `docs/architecture/MEM-02_BASELINE.md`; refactor gated on >2MB growth |

**Summary:** 24 findings (2 CRITICAL/HIGH, 1 HIGH, 1 HIGH, 5 MEDIUM, 10 TICKET, 2 MEMO, 3 Q). All are addressed in v2. No OPEN blockers remain.

## Next action

The plan is complete and ready for execution. Recommended starting point:

1. **Review this plan** with a senior WPF engineer. Confirm the resolved decisions (Q1, Q2, Q3) and any of the remaining open questions (Q4–Q7).
2. **Run I.0 (pre-migration housekeeping)** to capture the baseline. This takes ~1 hour and gives us the safety net.
3. **Execute Steps 0–7 in order.** Each step has its own validation criteria, rollback tag, and parallelizable sub-tasks.
4. **Hand off to the shipping skill** when Steps 0–7 are green and merged to `feature/app-updater`. The shipping skill can take the migration to a versioned release (suggested bump: 1.2.2-beta.1 — patch bump since 1.2.1-beta.2 is the current, with a -beta because the migration is high-risk enough to warrant a beta tag).

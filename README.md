# PoE 2 Overlay Companion (C# / .NET 8 WPF)

A clean, hardware-accelerated, non-activating Windows desktop overlay for **Path of Exile 2** (`v1.0.0`), built for **any build** with **zero hardcoded presets or bundled loot filters**.

Supports **Українська / English / Русский** (both UI and build breakdowns), **batch `.build` / PoB2 / `pobb.in` / guide URL import**, an **interactive 2D Passive Tree viewer with Mobalytics-style leveling diff**, **AI Loot Filter generator & `.filter` validator**, live **`Ctrl+C` item evaluation**, **Trade 2 search**, a **Farm Session Tracker**, **comprehensive build update diffing**, and an optional **multimodal AI Advisor**.

---

## Features

- **Zero Built-in Build or Filter Bias:** Clean universal PoE 2 tool out of the box — import your own builds via **Drag & Drop**, multi-file `.build` selection, folder import, **PoB2** codes / **`pobb.in`** / **`pastebin`** URLs, or guide links.
- **Interactive 2D Passive Tree (`poe2_tree_05.bin.gz` — 4,535 nodes):**
  - Fast compact binary reader (`BinaryReader`) + `WeakReference` cache & working-set memory trimming (`SetProcessWorkingSetSize`) when hidden/collapsed.
  - Zoomable and pannable dark canvas with hover tooltips (node name, Keystone/Notable/Ascendancy type, localized stats).
  - **Step-by-Step Leveling & Respec Diff:** Scrub through exact passive allocation steps (`1..N`) or compare **Current Stage vs Previous Stage** (`+ Take` in emerald, `Kept` in brass, `- Respec` in crimson).
  - Side panel with searchable **Keystones / Notables / Ascendancy order**.
- **AI Loot Filter Generator & Custom `.filter` Manager:**
  - Generate PoE 2 loot filters from your active build's damage scaling, armor/ES/evasion bases, and strictness tier (`1–4`) via AI, validate syntax (`LinkedSockets` PoE1 warnings, block count), edit live, and install straight to `Documents\My Games\Path of Exile 2\`.
  - Drag & Drop or browse any existing `.filter` file to validate and install it.
- **Full Localization (`UA | EN | RU`):** Instant switching translates **both** overlay controls and build data (equipment slots, defense types, passive node kinds, item modifiers, update changelogs, and AI output).
- **Batch Build Import & In-Game `BuildPlanner` Sync:**
  - Reads `.build` files strictly from `Documents\My Games\Path of Exile 2\BuildPlanner\` (live `FileSystemWatcher`).
  - Batch-adding `.build` files/folders automatically copies them into PoE 2's `BuildPlanner\` folder and lets you merge progression stages into **one multi-level profile** or keep them separate.
- **Universal `Ctrl+C` Item Evaluator, Interactive Mod Price-Check & Dynamic PoE 2 Trade:**
  - Compares items directly against your **actually worn gear** (`WornGearBySlot` via **"Equip as Mine"** or Official PoE 2 Account Sync) and displays explicit **`⬆ Better / ⬇ Worse (% delta)`** verdicts in the cursor Toast + asynchronous live Trade 2 price estimate.
  - **Interactive Mod Checkboxes (`Alt+E` / PoE Overlay II style):** toggle individual explicit/defense mods to query or open an exact custom Trade 2 search, plus 1-click Vendor & Waystone Regex (`<50` chars).
- **Trade Helper Bar, Campaign Zone Guide (`Client.txt`), Stash Net-Worth & Command Wheel (`F8`):**
  - Parses incoming/outgoing PoE 2 trade whispers from `Client.txt` with a floating 1-click macro bar (*Invite*, *Trade*, *Hideout*, *1 min*, *Thx*, *Sold*).
  - Automatically displays permanent campaign zone rewards (`+30 Spirit`, `+2 Passives`, `+10% Resists`) and boss tips on zone transition.
  - Scans PoE 2 Stash tabs through your signed-in `pathofexile.com` WebView2 session for **Stash Net-Worth (Div / Ex/hr)** and adds an **`F8` Quick Command Wheel** (`/hideout`, `/kingsmarch`, `/remaining`, `@last thx`, custom macros).
- **Real Build Update Monitor:**
  - Tracks structural diffs across **all** added builds every 2–3 days: added/removed passives (resolved to real node names), skill/support link edits, gear updates, online `pobb.in` / PoB updates, and guide text diffs (`Ctrl+A → Ctrl+C`).
- **Native Win32 Non-Activating Window (`WindowChrome` + DWM) & Triple-Tier Screen Capture:**
  - `WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE -> MA_NOACTIVATE`, hardware Windows 11 DWM rounded corners, `LWA_ALPHA` opacity, `SetWinEventHook` foreground topmost, System Tray icon (`Shell_NotifyIconW`), and Single-Instance Mutex.
  - Captures strictly PoE 2's client window using **DXGI Desktop Duplication (HDR Reinhard tone-mapping)** → **DWM `PrintWindow`** → **GDI `BitBlt`** fallback, encoded asynchronously off the UI thread (`%LocalAppData%\Poe2OverlayCompanion\overlay_config.json`).

---

## Global Hotkeys

| Key | Action |
|---|---|
| `F8` | Open Quick Command Wheel (`/hideout`, `/remaining`, `@last`) |
| `F9` | Collapse / Expand overlay (or exit Mini-HUD) |
| `F10` | Toggle Click-Through lock |
| `F11` | Capture PoE 2 client window & ask AI Advisor |
| Custom Key | Optional configurable Buff Timer (`BuffTimer` in Settings) |

---

## Quick Start (3 Release Downloads Explained)

- **`Poe2OverlayCompanion_Setup_1.0.0.exe` (Windows Installer):** Checks [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), installs to `%LocalAppData%\Programs\PoE 2 Overlay Companion\`, creates Start Menu & Desktop shortcuts, adds standard Windows Uninstaller, and supports Launch with Windows.
- **`Poe2OverlayCompanion.exe` (Standalone Single-File EXE):** Runs directly with 1 click without installation (stores config in `%LocalAppData%\Poe2OverlayCompanion\` unless launched with `--portable`).
- **`Poe2OverlayCompanion_Portable_v1.0.0.zip` (Portable Archive):** Extracts anywhere with a `portable.mode` marker so config, builds, and WebView2 cache stay strictly inside `.\Data\` next to the `.exe`.

### Option C — Build Standalone `.exe` & Installer from Source
```powershell
dotnet publish Poe2OverlayCompanion.csproj -c Release -r win-x64 --self-contained false -o dist
.\dist\Poe2OverlayCompanion.exe
# Optional (requires Inno Setup 6):
iscc installer\Poe2OverlaySetup.iss
```

---

## Project Structure
```text
├── Assets/
│   ├── app.ico                        # Multi-res embedded app/tray icon
│   └── poe2_tree_05.bin.gz            # Compact binary 4,535-node PoE 2 passive tree dataset
├── Controls/PassiveTreeView.cs        # GPU-accelerated DrawingVisual zoom/pan passive tree
├── Localization/
│   ├── Loc.cs                         # UA / EN / RU UI strings
│   └── PoeTermLocalizer.cs            # Build slot, stat & passive node localizer
├── Models/BuildModels.cs              # Build profiles, stages, passive diffs & changelogs
├── Services/
│   ├── BuildPlannerService.cs         # Official Documents\My Games\...\BuildPlanner .build reader
│   ├── BuildProfileManager.cs         # Batch .build, PoB2, pobb.in & guide URL importer
│   ├── BuildUpdateMonitorService.cs   # Structural build diff monitor (files, PoB, URLs, clipboard)
│   ├── ClientLogWatcher.cs            # Incremental Client.txt & FindWindow game detector
│   ├── GeminiAdvisorService.cs        # Gemini + OpenAI-compatible multimodal client
│   ├── ItemEvaluator.cs               # Universal build-aware Ctrl+C item evaluator
│   ├── LootFilterService.cs           # AI Loot Filter generator, validator & .filter installer
│   ├── PassiveTreeService.cs          # Binary passive tree index, WeakRef cache & progression diff
│   ├── Pob2Parser.cs                  # PoB2 Base64/zlib XML & pobb.in/pastebin fetcher
│   ├── ScreenCaptureService.cs        # Triple-tier DXGI(HDR)/PrintWindow/BitBlt capture + async JPEG
│   ├── TradeLinkBuilder.cs            # Dynamic PoE 2 Trade search URL builder
│   ├── TrayIconService.cs             # Native Shell_NotifyIconW tray menu & Windows autostart
│   ├── UiBrushes.cs                   # Frozen PoE 2 theme brush cache
│   └── Win32OverlayHelper.cs          # WS_EX_NOACTIVATE, LWA_ALPHA, DWM, WinEventHook & WS trim
├── NativeMethods.txt / .json          # CsWin32 source-generated DXGI/D3D11 interop config
├── installer/Poe2OverlaySetup.iss     # Inno Setup script (UA / EN / RU wizard)
├── .github/workflows/build.yml        # GitHub Actions CI + Release builder
└── MainWindow / MiniHudWindow / ToastWindow
```

## Fair Play & License
Read-only external companion (zero memory reading, zero input injection) — see [SECURITY.md](SECURITY.md).  
Licensed under MIT — see [LICENSE](LICENSE).

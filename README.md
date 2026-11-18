# PoE 2 Overlay Companion (C# / .NET 8 WPF)

A clean, hardware-accelerated, non-activating Windows desktop overlay for **Path of Exile 2** (`v1.1.3`), built for **any build** with **zero hardcoded presets or bundled loot filters**.

Supports **Українська / English / Русский** (both UI and build breakdowns), **batch `.build` / PoB2 / `pobb.in` / guide URL import**, an **interactive 2D Passive Tree viewer with Mobalytics-style leveling diff**, **AI Loot Filter generator & `.filter` validator**, live **`Ctrl+C` item evaluation**, **Trade 2 search**, a **Farm Session Tracker**, **comprehensive build update diffing**, and an optional **multimodal AI Advisor**.

---

## Features

- **Zero Built-in Build or Filter Bias:** Clean universal PoE 2 tool out of the box — import your own builds via **Drag & Drop**, multi-file `.build` selection, folder import, **PoB2** codes / **`pobb.in`** / **`pastebin`** URLs, or guide links.
- **Interactive 2D Passive Tree (`poe2_tree_05.bin.gz` v2 — 4,546 nodes & 120+ FPS):**
  - Official `0_5/tree.json` geometry with true concentric orbital arcs (`ArcTo`), attribute-colored sockets, central Class Medallion, multi-layer glowing gold paths, and thread-safe lazy cache + pre-baked `RenderTargetBitmap` texture for silky-smooth pan/zoom.
  - **Step-by-Step Leveling & Respec Diff:** Scrub through exact passive allocation steps (`1..N`) or compare **Current Stage vs Previous Stage** (`+ Take` in emerald, `Kept` in brass, `- Respec` in crimson, plus `Set 1 / Set 2` filter).
  - Side panel with searchable **Keystones / Notables / Ascendancy order** and shortest-path (`+N pts`) BFS hover preview.
- **AI Loot Filter Generator & Custom `.filter` Manager:**
  - Generate PoE 2 loot filters from your active build's damage scaling, armor/ES/evasion bases, and strictness tier (`1–4`) via AI, validate syntax (`LinkedSockets` PoE1 warnings, block count), edit live, and install straight to `Documents\My Games\Path of Exile 2\`.
  - Drag & Drop or browse any existing `.filter` file to validate and install it.
- **Full Localization (`UA | EN | RU`):** Instant switching translates **both** overlay controls and build data (equipment slots, defense types, passive node kinds, item modifiers, update changelogs, and AI output).
- **Batch Build Import & In-Game `BuildPlanner` Sync:**
  - Reads `.build` files strictly from `Documents\My Games\Path of Exile 2\BuildPlanner\` (live `FileSystemWatcher`).
  - Batch-adding `.build` files/folders automatically copies them into PoE 2's `BuildPlanner\` folder and lets you merge progression stages into **one multi-level profile** or keep them separate.
- **Universal `Ctrl+C` Item Evaluator, Waystone Mod Checker, Interactive Price-Check & PoE2DB Lookup (`Sidekick` / `Exiled Exchange 2` style):**
  - Compares items directly against your **actually worn gear** (`WornGearBySlot` via **"Equip as Mine"** or Official PoE 2 Account Sync) and displays explicit **`⬆ Better / ⬇ Worse (% delta)`** verdicts in the cursor Toast + asynchronous live Trade 2 price estimate.
  - **Interactive Waystone / Map Mod Checker:** Automatically flags dangerous Waystone modifiers (`Deadly` / `Caution` / `Good`) for your build on `Ctrl+C`; click any mod in the Items tab to save custom overrides (`CustomWaystoneModTags`).
  - **Live Trade 2 Listings with Age + Pseudo Mods & Editable `min` Thresholds:** Toggle explicit, defense, or pseudo-total stats (`Total Elemental Resistances`, `Total Attributes`), adjust numeric `min` boxes inline before searching, and inspect up to 8 live listing prices with listing ages (`4m`, `2h`, `6d`) to avoid stale price-fixers.
  - **Affix Slot Counter & 1-Click `PoE2DB` Lookup:** Shows estimated `Prefix / Suffix` occupancy + open Exalt slots on Magic/Rare gear and opens the exact item/base on `poe2db.tw`.
  - **Live Currency Exchange Ticker (`Farm` tab):** Displays live `Divine -> Exalted / Chaos` and `Chaos -> Exalted` market ratios for your active league (`FetchKeyExchangeRatesAsync`), plus 1-click Vendor & Waystone Regex (`<50` chars).
- **Trade Helper Bar, Campaign Zone Guide (`Client.txt`), Stash Net-Worth & Command Wheel (`F8`):**
  - Parses incoming/outgoing PoE 2 trade whispers from `Client.txt` with a floating 1-click macro bar (*Invite*, *Trade*, *Hideout*, *1 min*, *Thx*, *Sold*).
  - Automatically displays permanent campaign zone rewards (`+30 Spirit`, `+2 Passives`, `+10% Resists`) and boss tips on zone transition.
  - Scans PoE 2 Stash tabs through your signed-in `pathofexile.com` WebView2 session for **Stash Net-Worth (Div / Ex/hr)** and adds an **`F8` Quick Command Wheel** (`/hideout`, `/kingsmarch`, `/remaining`, `@last thx`, custom macros).
- **Real Build Update Monitor:**
  - Tracks structural diffs across **all** added builds every 2–3 days: added/removed passives (resolved to real node names), skill/support link edits, gear updates, online `pobb.in` / PoB updates, and guide text diffs (`Ctrl+A → Ctrl+C`).
- **Native Win32 Non-Activating Layered Window & Triple-Tier Screen Capture:**
  - `WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE -> MA_NOACTIVATE`, adjustable window opacity (`AllowsTransparency`), `SetWinEventHook` foreground topmost, System Tray icon (`Shell_NotifyIconW`), and Single-Instance Mutex.
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

- **`Poe2OverlayCompanion_Setup_1.1.3.exe` (Windows Installer):** Checks [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), installs to `%LocalAppData%\Programs\PoE 2 Overlay Companion\`, creates Start Menu & Desktop shortcuts, adds standard Windows Uninstaller, and supports Launch with Windows.
- **`Poe2OverlayCompanion.exe` (Standalone Single-File EXE):** Runs directly with 1 click without installation (stores config in `%LocalAppData%\Poe2OverlayCompanion\` unless launched with `--portable`).
- **`Poe2OverlayCompanion_Portable_v1.1.3.zip` (Portable Archive):** Extracts anywhere with a `portable.mode` marker so config, builds, and WebView2 cache stay strictly inside `.\Data\` next to the `.exe`.

### Option C — Build Standalone `.exe` & Installer from Source
```powershell
dotnet publish Poe2OverlayCompanion.csproj -c Release -r win-x64 --self-contained false -p:DebugType=none -p:AllowedReferenceRelatedFileExtensions=none -o dist
.\dist\Poe2OverlayCompanion.exe
# Optional (requires Inno Setup 6):
iscc installer\Poe2OverlaySetup.iss
```

---

## Project Structure
```text
├── Assets/
│   ├── app.ico                        # Multi-res embedded app/tray icon
│   ├── poe2_tree_05.bin.gz            # Compact binary 4,546-node PoE 2 passive tree dataset
│   └── poe2_gems_05.json.gz           # Compressed 958-gem PoE 2 (0.5) catalog from PoE2DB
├── Controls/PassiveTreeView.cs        # GPU-accelerated DrawingVisual zoom/pan passive tree
├── Localization/
│   ├── Loc.cs                         # UA / EN / RU UI strings
│   └── PoeTermLocalizer.cs            # Build slot, stat & passive node localizer
├── Models/BuildModels.cs              # Build profiles, stages, passive diffs & changelogs
├── Services/
│   ├── AppUpdateService.cs            # GitHub Release self-updater (Setup EXE & Portable ZIP)
│   ├── BuildPlannerService.cs         # Official Documents\My Games\...\BuildPlanner .build reader
│   ├── BuildProfileManager.cs         # Batch .build, PoB2, pobb.in & guide URL importer
│   ├── BuildUpdateMonitorService.cs   # Structural build diff monitor (files, PoB, URLs, clipboard)
│   ├── CampaignGuideDb.cs             # Acts 1–4 & Interludes 1–3 permanent rewards & boss guide
│   ├── ClientLogWatcher.cs            # Incremental Client.txt & FindWindow game detector
│   ├── GeminiAdvisorService.cs        # Gemini + OpenAI-compatible multimodal client
│   ├── ItemEvaluator.cs               # Universal build-aware Ctrl+C item evaluator
│   ├── LivePoe2DataService.cs         # Live GGG `/api/trade2/data/items` cache (uniques, bases, gems)
│   ├── LootFilterService.cs           # AI Loot Filter generator, validator & .filter installer
│   ├── MobalyticsImportService.cs     # Interactive WebView2 Mobalytics build variant scraper
│   ├── PassiveTreeService.cs          # Binary passive tree index, lazy cache & progression diff
│   ├── Pob2Parser.cs                  # PoB2 Base64/zlib XML & pobb.in/pastebin fetcher
│   ├── Poe2GemsDb.cs                  # Full 958-gem PoE 2 (0.5) tier, attribute & obtain resolver
│   ├── Poe2KnowledgeDb.cs             # Curated + live GGG unique/base obtain & crafting database
│   ├── PoeAccountService.cs           # Live PoE 2 character, gear, weapon DPS & stat sync
│   ├── PoeTradePriceService.cs        # Live Trade 2 search/fetch, leagues & poe.ninja exchange rates
│   ├── ScreenCaptureService.cs        # Triple-tier DXGI(HDR)/PrintWindow/BitBlt capture + async JPEG
│   ├── StashTrackerService.cs         # Currency & fragment stack valuation helper
│   ├── Trade2StashService.cs          # PoE 2 public/Merchant stash scanner via Trade 2 API
│   ├── TradeLinkBuilder.cs            # Dynamic PoE 2 Trade search URL builder
│   ├── TrayIconService.cs             # Native Shell_NotifyIconW tray menu & Windows autostart
│   ├── UiBrushes.cs                   # Frozen PoE 2 theme brush cache
│   ├── WaystoneModChecker.cs          # Build-aware Waystone / Map mod danger analyzer
│   ├── WebView2Helper.cs              # Shared GPU-safe WebView2 environment & script runner
│   └── Win32OverlayHelper.cs          # WS_EX_NOACTIVATE, opacity, DWM, WinEventHook & chat macros
├── NativeMethods.txt / .json          # CsWin32 source-generated DXGI/D3D11 interop config
├── installer/Poe2OverlaySetup.iss     # Inno Setup script (UA / EN / RU wizard)
├── .github/workflows/build.yml        # GitHub Actions CI + Release builder
└── MainWindow / MiniHudWindow / ToastWindow
```

## Fair Play & License
Read-only external companion (zero memory reading, zero input injection) — see [SECURITY.md](SECURITY.md).  
Licensed under MIT — see [LICENSE](LICENSE).

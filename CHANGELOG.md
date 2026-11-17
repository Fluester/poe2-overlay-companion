# Changelog

## v1.1.0 (Bootstrap 5 Dark UI, Separate Live Account Sync, Stash Direct Scan & GitHub Auto-Updater)
- **GitHub Self-Updater (`AppUpdateService`):** Checks `Fluester/poe2-overlay-companion` releases automatically and adds 1-click self-updating from Settings for both installed setups and Portable ZIP builds.
- **Streamlined Bootstrap 5.3 Dark UI:** Decluttered navigation into 7 clean tabs (merging Build Profiles, `.build` Planner, and Updates into **Білди**), simplified header bar, and unified dark theme (`#16191D` / `#212529` / `#0D6EFD`) across all overlay windows.
- **PoE 2 Account Separated from Builds + Direct Stash Scan:** Account sync (`wxor`) now populates live level, league, worn gear, and skills cleanly without overwriting or generating `[Account]` build profiles; Stash net-worth scans via WebView2 cookies + hidden JSON fetch without opening PoE 1 pages; fixes Flask obtain classification, stage diagnostics, and adds 2-column in-overlay gear comparison.
- **PoE 2 Passive Tree v2 (Screenshot Fidelity & 120+ FPS):**
  - Rebuilt embedded `poe2_tree_05.bin.gz` (`v2`, 4,546 nodes) from official `0_5/tree.json` using 1-based group indexing (`0` misplaced cross-map edges) and exact orbital arc math (`ConnectionOrbits` + `OrbitRadii`).
  - Redesigned `PassiveTreeView` with pitch-black constellation canvas, central Class Medallion, attribute-tinted sockets, glowing multi-layer gold active branches, Weapon Set 1/2 paths, baked `RenderTargetBitmap` texture + close-up vector LOD for zero-lag pan/zoom.
- **Full PoE 2 Account Sync & WebView2 Black-Screen Fix (`PoeAccountService` + `WebView2Helper`):**
  - Shared `WebView2Helper` disables `CalculateNativeWinOcclusion` & GPU DirectComposition swapchain black-screen loops, adds browser nav bar + external browser fallback, and isolates Z-order from `WS_EX_NOACTIVATE` topmost hooks.
  - Pulls PoE 2 character data via `/internal-api/my-account/characters` & `/internal-api/my-account/character/{id}?realm=poe2` (+ `character-window` fallback): Level, XP, League, Weapon Set 1 & 2, Armor, Rings 1–3, Flasks 1–2, Charms 1–3, socketed Runes, Active Skills & Support Gems (Lv/Q%), and aggregated defense/resist summary.
- **Interactive Waystone / Map Mod Checker (`WaystoneModChecker`):** Automatically classifies Waystone modifiers (`Good` / `Caution` / `Deadly`) against your active build on `Ctrl+C`, warns in the cursor toast, and lets you click any mod in the Items tab to cycle & persist custom safety tags (`CustomWaystoneModTags`).
- **Pseudo Total Stats & Editable `min` Thresholds in Custom Price-Check:** Added Trade 2 pseudo stats (`Total Elemental Resistance`, `Total Attributes`) plus inline numeric `min` input boxes next to each selectable mod filter (`GetCheckedMods` / `BuildCustomTradeQueryJson`).
- **Live Listings Table with Listing Age (`TradePriceListing`):** Trade 2 searches now parse `listing.indexed` timestamps and display up to 8 live listing prices with exact ages (`4m`, `2h`, `6d`) in the UI and cursor toast to spot stale price-fixing offers.
- **Affix Slot Counter & 1-Click `PoE2DB` Lookup:** Estimates `~Prefix / ~Suffix` counts and open Exalt slots on Rare/Magic items (`AffixSummary`) and adds a 1-click `PoE2DB` button in the Items tab.
- **Live Currency Exchange Ticker (`PageFarm`):** Fetches and caches live `1 Divine -> Ex / Chaos` and `1 Chaos -> Ex` exchange rates for your active league directly inside the Farm tab.

## v1.0.0 (Initial Public Release)
- **Zero Built-in Presets (Universal Out of the Box):** No hardcoded build presets or bundled `.filter` files in the app, Windows Installer, or repository. Import any PoE 2 build(s) and generate or load your own loot filter.
- **Interactive 2D Passive Tree (`PassiveTreeView` + Compact Binary `poe2_tree_05.bin.gz`):**
  - Renders all 4,535 PoE 2 passive nodes with smooth cursor zoom, drag pan, auto-center, and stat tooltips.
  - Binary tree reader (`BinaryReader`) with `WeakReference<PassiveTreeIndex>` and automatic working-set memory trimming when idle or minimized.
  - Step-by-step leveling slider (`1..N`) and stage-vs-stage progression diff (`+ Take`, `Kept`, `- Respec`) + searchable Keystones/Notables path.
- **AI Loot Filter Generator & `.filter` Manager (`LootFilterService`):**
  - Generate custom PoE 2 NeverSink-syntax loot filters with AI tailored to your active build, strictness level (`1–4`), and custom rules, or import/validate existing `.filter` files directly into `Documents\My Games\Path of Exile 2\`.
- **Full Localization (`UA / EN / RU`) for Build Content:** `PoeTermLocalizer` translates both UI controls and build data (equipment slots, defense styles, weapon classes, passive node types, modifiers, PoB2 analysis, item evaluation, and update changelogs).
- **Batch Build Import & Drag-and-Drop:** Supports multi-file `.build` selection, folder import, Drag & Drop onto the overlay (`.build` and `.filter`), multi-line PoB2/`pobb.in`/`pastebin`/URL list import, and merging progression stages into one profile synced to character level.
- **Real Build Update Monitor:** Tracks SHA-256 + structural diffs across `.build` skills/supports/gear/passives, `pobb.in` PoB updates, guide URL snapshots, and clipboard guide diffs every 2–3 days.
- **Triple-Tier Hardware Screen Capture:** Uses DXGI Desktop Duplication (including HDR `R16G16B16A16_FLOAT` Reinhard tone-mapping) -> DWM `PrintWindow` -> GDI `BitBlt` fallback for AI screen coaching.
- **Standalone Win32/WPF Overlay & Windows Installer (`v1.0.0`):** Hardware-accelerated `WindowChrome` with `WS_EX_NOACTIVATE`, DWM rounded corners, Windows System Tray integration, single-instance mutex, and Inno Setup installer.

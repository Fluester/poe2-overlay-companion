# Changelog

## v1.1.1 (Full 958-Gem PoE2DB 0.5 Catalog, Live GGG Item/Unique Sync, Trade 2 Stash Scanner & Account Sync Fixes)
- **Full 958-Gem PoE 2 (0.5) Catalog (`poe2_gems_05.json.gz` + `Poe2GemsDb`):**
  - Embedded compressed 958-gem PoE2DB 0.5 dataset (`55.6 KB` gzip) covering all Active Skills, Spirit Buffs, Meta Triggers, Support Gems (Tiers I–V), and Lineage Supports with exact Uncut tiers, character level curves (`1..90`), Str/Dex/Int progression, Spirit reservation, and cost multipliers.
  - Fixed fallback gem level bug where `stageMinLevel` was treated as Uncut Gem Tier (e.g. `Fangs of Frost` now accurately resolves as `Uncut Skill Gem Lv 3`, Char Lvl 6+, 14 Dex, Act 1).
- **Live GGG Trade 2 Item, Unique & Gem Catalog (`LivePoe2DataService`):**
  - Automatically syncs and caches official `/api/trade2/data/items` data (`464` unique item names, `2,225` base types, `905` gems) in the background (`live_trade2_items.json`, 24h TTL) to back up `Poe2KnowledgeDb` and `Poe2GemsDb` with `Orb of Chance` base hints and future patch gems.
- **PoE 2 Account Sync Normalization & Stash Scanner (`PoeAccountService` + `Trade2StashService`):**
  - Unwraps object-formatted modifiers (`{ description }`), strips `[Key|Label]` markup, interpolates `{0}/{1}` flask/charm values, extracts `requirements` & `utilityMods`, calculates elemental/chaos weapon DPS, and prevents empty responses from wiping saved gear.
  - Added `Trade2StashService` to scan public/Merchant listed stash tabs via `/api/trade2/search/poe2/{league}` + `/api/trade2/fetch` and filter out PoE 1 Standard responses (`IsGenuinePoe2StashTabJson`).
- **Act 4 & Interludes 1–3 Campaign Guide + UI Polish:**
  - Updated `CampaignGuideDb` and `PoeAccountService.BuildStageAdvice` for PoE 2's 4-Act + 3-Interlude structure (including `Lythara` in Interlude 3 Kriar Village for `+40 Spirit` = `100` base Spirit total, 14 permanent milestones).
  - Fixed Settings `Opacity` cycling (`95% → 80% → 65% → 48% → 100%` with `AllowsTransparency` and config persistence), added collapsible `Trade / Mods ▾` drawer in Items, explained `Client.txt` in tooltip, completed `UA / EN / RU` localization across all tabs, and stripped `.pdb`/`.xml` bloat from release publishes.

## v1.1.0 (Bootstrap 5 Dark UI, Separate Live Account Sync, Stash Direct Scan & GitHub Auto-Updater)
- **GitHub Self-Updater (`AppUpdateService`):** Checks `Fluester/poe2-overlay-companion` releases automatically and adds 1-click self-updating from Settings for both installed setups and Portable ZIP builds.
- **Streamlined Bootstrap 5.3 Dark UI:** Decluttered navigation into 7 clean tabs (merging Build Profiles, `.build` Planner, and Updates into **Білди**), simplified header bar, and unified dark theme (`#16191D` / `#212529` / `#0D6EFD`) across all overlay windows.
- **PoE 2 Account Separated from Builds + Direct Stash Scan:** Account sync now populates live level, league, worn gear, and skills cleanly without overwriting or generating `[Account]` build profiles; Stash net-worth scans via WebView2 cookies + hidden JSON fetch without opening PoE 1 pages; fixes Flask obtain classification, stage diagnostics, and adds 2-column in-overlay gear comparison.
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

# Changelog

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

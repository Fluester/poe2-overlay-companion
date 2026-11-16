# Security & Fair Play Policy

## API Key Storage
- All API keys (Google Gemini or OpenAI-compatible Bearer tokens) and user settings are stored **locally only** in `%LocalAppData%\Poe2OverlayCompanion\overlay_config.json`.
- API keys are never embedded in the repository, source code, or telemetry.

## Path of Exile 2 ToS Compliance
- **Zero memory reading/writing:** This application never attaches to, injects into, or reads the process memory of Path of Exile 2.
- **Zero input automation:** The overlay never sends simulated clicks, keystrokes, or automated macros to the game client.
- **Read-only integration only:** It uses official/sanctioned mechanisms:
  - Tailing `logs/Client.txt`
  - Reading clipboard text copied manually with `Ctrl+C`
  - Reading `.build` JSON files in `Documents\My Games\Path of Exile 2\BuildPlanner\`
  - Capturing screenshots for on-demand AI advice

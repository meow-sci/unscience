# skittles.lib — Skittles Core Library

Headless core for the Skittles global ImGui theme manager. No UI, no StarMap, no Harmony — just theme data, I/O, and application logic.

## Public API

### `ThemeDefinition`
POCO class representing a complete ImGui visual state snapshot.
- Stores all 60 `ImGuiCol` color values as `float[][]` (RGBA 0–1)
- Stores all style variables (float, float2, bool properties)
- `static ThemeDefinition CaptureFromImGui()` — snapshot current global style
- `void ApplyToImGui()` — write all values to global style

### `ThemeSerializer` (static)
TOML serialization using Tomlyn 0.19.
- `static string Serialize(ThemeDefinition)` — to TOML string
- `static ThemeDefinition? Deserialize(string)` — from TOML string
- `static void SaveToFile(ThemeDefinition, string path)` — serialize + write
- `static ThemeDefinition? LoadFromFile(string path)` — read + deserialize

### `ModConfig` / `ModConfigSerializer`
Tiny config for persisting the active theme name.
- `ModConfig.ActiveThemeName` — the last applied theme
- `ModConfigSerializer.SaveToFile` / `LoadFromFile` — TOML I/O

### `ThemeManager`
Orchestrates all theme operations. Instantiate and call `Initialize()` once.
- `void Initialize()` — capture default, ship presets, load config, apply startup theme
- `string[] GetThemeNames()` — ordered list for a UI combobox (built-ins first, then custom)
- `void ApplyTheme(string name)` — apply by name; handles built-in shortcuts (Dark/Light/Classic/Game Default)
- `void SaveCurrentAsTheme(string name)` — capture + save current style as custom theme
- `void RefreshThemeList()` — re-scan disk for custom themes
- `void RestoreDefaults()` — restore the captured game default
- `string? ActiveThemeName` — currently applied theme name

### `ThemeEntry`
Members of the `AvailableThemes` list: `Name`, `IsBuiltIn`, `FilePath?`.

### `BuiltInThemes` (static)
- `static ThemeDefinition InanimateCarbonRod()` — radioactive terminal preset

## Scene saves

Captures the full applied ImGui style, including edits not yet stored as a named theme. Save load validates the detached style and reapplies it; a native save without state restores the user's configured startup theme. Named themes and theme preference files remain global.

Scene persistence is registered by the Unscience host through `ISaveParticipantSource`; standalone development hosts retain their existing lifecycle. Use ordinary KSA Save/Load. See [save behavior](../unscience/README.md#scene-saves) and the [design plan](../plans/SAVES.md).

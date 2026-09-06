# Kitchen Sink

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

A collection of one-off hacks and fixes for KSA that don't warrant their own dedicated mod.

## Overview

**Toggle window:** F11

## Features

### Fix Invisible Subparts

Workaround for a KSA bug where vehicle subparts become invisible in the editor.

**How to use:**
1. Open the vehicle editor in KSA.
2. Open the Kitchen Sink window (F11).
3. Click **Refresh Vehicle** to call `ReinitializeDerivedValues` on the editor's part tree.

This forces the game to recompute all derived values for the part tree, which restores visibility of subparts that have gone invisible due to the bug.

### Force IVA Rendering

Forces interior (IVA) parts to render even when not in IVA camera mode. After a KSA update, interior parts only render when the camera is in IVA mode — this feature restores the old behavior.

Kitchen Sink also keeps IVA meshes visible in the vehicle editor. The editor renders through the main viewport rather than an IVA camera, so KSA's default `PartModel.AddInstance` check can otherwise skip internal SubPart meshes even though they are needed for part authoring.

**How it works:**
- When toggled ON, directly mutates `Template.Internal = false` on all loaded `PartModel` instances so the game's rendering check no longer skips them.
- A Harmony postfix on the `PartModel` constructor catches parts created after the toggle is enabled.
- A Harmony postfix on `PartModel.AddInstance` re-adds internal mesh instances while `Program.Editor` is active, fixing IVA SubParts that the editor preview would otherwise hide.
- When toggled OFF (or on mod unload), all mutated templates are restored to `Internal = true`.

**How to use:**
1. Open the Kitchen Sink window (F11).
2. Check **Always Render IVA Interiors** to enable.

## Architecture

- `kitchen-sink.lib/KitchenSinkLib.cs` — `KitchenSinkSubmod` implementing `ISubmod` with all feature logic.
- `kitchen-sink/Mod.cs` — StarMap lifecycle host; owns the F11 window toggle.
- `kitchen-sink/Patcher.cs` — HotkeyGuard setup.

| `Patcher.cs` | Harmony-based runtime patching setup |
| `kitchen-sink.csproj` | Main mod project |
| `kitchen-sink.lib/KitchenSinkLib.cs` | Library class (headless logic) |

### Mod Lifecycle

```
OnImmediateLoad()        → Called first, before any other mods
  ↓
OnFullyLoaded()          → All mods loaded, safe to access others
  ↓
OnBeforeGui() / OnAfterUi()  → Render ImGui every frame
  ↓
Unload()                 → Cleanup, remove patches
```

## Architecture

### Mod.cs
Entry point for the mod with lifecycle management.

```csharp
public class Mod : StarMapMod
{
    public override void OnImmediateLoad()
    {
        // First initialization
        Console.WriteLine("Fixme-Mod-Name: OnImmediateLoad");
    }
    
    public override void OnFullyLoaded()
    {
        // All mods ready, initialize partnerships
        Patcher.Initialize();
    }
    
    public override void OnAfterUi()
    {
        // Render ImGui window every frame
        RenderWindow();
    }
    
    public override void Unload()
    {
        // Cleanup patches and resources
        Patcher.Cleanup();
    }
    
    private void RenderWindow()
    {
        if (!showWindow) return;
        
        ImGui.SetNextWindowSize(new Vector2(400, 200), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Fixme-Mod-Name", ref showWindow))
        {
            ImGui.Text("Hello, World!");
            if (ImGui.Button("Click Me!"))
            {
                Console.WriteLine("Button clicked!");
            }
            ImGui.End();
        }
    }
}
```

### Patcher.cs
Harmony-based runtime method patching initialization.

```csharp
public static class Patcher
{
    private static Harmony harmony;
    
    public static void Initialize()
    {
        harmony = new Harmony("MeowSci.KitchenSink");
        harmony.PatchAll();  // Patches defined in assembly
    }
    
    public static void Cleanup()
    {
        harmony?.UnpatchAll();
    }
}
```

### Library Project (optional)
Separate `.lib` project for reusable, headless logic:

```csharp
public static class KitchenSinkLib
{
    public static void DoSomething()
    {
        // Reusable functionality
    }
}
```

## Getting Started with This Template

### Step 1: Rename
```
kitchen-sink → your-cool-mod
KitchenSink → YourCoolMod
MeowSci.KitchenSink → MeowSci.YourCoolMod
```

### Step 2: Update Project Files
- Rename `.csproj` files
- Update assembly names
- Update namespace declarations

### Step 3: Implement Mod Logic
Replace template code with actual mod features:
- Define what should happen in each lifecycle method
- Add ImGui controls in `RenderWindow()`
- Implement Harmony patches in `Patcher.cs`

### Step 4: Document
Refer to this README structure and update with:
- Mod overview
- Features
- Architecture explanation
- Usage examples
- Implementation details

## Standard Mod Pattern

Most mods follow this pattern:

1. **Mod.cs**: UI + Lifecycle (StarMapMod subclass)
2. **Patcher.cs**: Runtime patches (Harmony setup)
3. **Lib project**: Reusable logic (separate assembly)
4. **README.md**: Documentation (what you're reading)

## ImGui Window Pattern

Standard toggle pattern:

```csharp
private bool showWindow = false;

public override void OnAfterUi()
{
    // F11 toggles window visibility
    if (Input.GetKeyDown(KeyCode.F11))
        showWindow = !showWindow;
    
    if (!showWindow) return;
    
    ImGui.SetNextWindowSize(new Vector2(400, 300), ImGuiCond.FirstUseEver);
    if (ImGui.Begin("Mod Name", ref showWindow))
    {
        // Render content here
        ImGui.End();
    }
}
```

## Harmony Patching Pattern

Basic patch structure:

```csharp
[HarmonyPatch(typeof(TargetClass), nameof(TargetClass.TargetMethod))]
public static class TargetMethodPatch
{
    public static bool Prefix(/* method parameters */)
    {
        // Prefix runs before original, return false to skip original
        Console.WriteLine("Before TargetMethod");
        return true;
    }
    
    public static void Postfix(/* method parameters */)
    {
        // Postfix runs after original
        Console.WriteLine("After TargetMethod");
    }
}
```

## Key Files for Reference

When developing from this template, refer to:

1. **[REPOSITORY_INDEX.md](../REPOSITORY_INDEX.md)** - All mods documentation
2. **sibling mod READMEs** - Similar mods for reference implementation
3. **HarmonyLib docs** - Runtime patching patterns
4. **ImGui API docs** - UI widget reference

## Next Steps

1. Copy this entire folder
2. Rename appropriately
3. Implement your feature logic
4. Test with `dotnet build`
5. Update this README with your mod's actual purpose and features

## Testing

Build the solution:
```bash
dotnet build
```

Check for compilation errors before continuing with implementation.

## Common Issues

- **Namespace mismatches**: Update everywhere (csproj, Mod.cs, Patcher.cs)
- **Project references**: Add library project reference to main mod
- **Harmony ID conflicts**: Each Harmony instance needs unique ID string
- **ImGui crashes**: Ensure ImGui calls only happen in OnAfterUi

## Notes for Developers

- Keep UI separate from logic (UI in Mod.cs, logic in Lib project)
- Use Console.WriteLine for debugging
- Test Harmony patches carefully—they affect game runtime
- Document your Harmony patches explaining what they do
- Consider performance impact of per-frame operations

## Related Mods

See similar template mods:
- [unscience](../unscience) - Minimal template without .lib
- Other mods for inspiration on complete implementations

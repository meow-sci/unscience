# kitchen-sink.lib

Feature implementation used by Unscience and the development host. See [feature documentation](../kitchen-sink/README.md) for controls and architecture.

## Scene saves

Saves the Force IVA Rendering switch. Reset restores shared template flags before another scene loads. The one-shot editor refresh and experimental Flexo diagnostic activity are not replayed.

Scene persistence is registered by the Unscience host through `ISaveParticipantSource`; standalone development hosts retain their existing lifecycle. Use ordinary KSA Save/Load. See [save behavior](../unscience/README.md#scene-saves) and the [design plan](../plans/SAVES.md).

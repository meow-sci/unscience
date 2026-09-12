# glass.lib

Feature implementation used by Unscience and the development host. See [feature documentation](../glass/README.md) for controls and architecture.

## Scene saves

Saves FOV degrees and whether the override is enabled. Restores the normal lens controls and applies the saved override to the reconstructed main camera.

Scene persistence is registered by the Unscience host through `ISaveParticipantSource`; standalone development hosts retain their existing lifecycle. Use ordinary KSA Save/Load. See [save behavior](../unscience/README.md#scene-saves) and the [design plan](../plans/SAVES.md).

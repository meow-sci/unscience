# i-feel-seen.lib

Feature implementation used by Unscience and the development host. See [feature documentation](../i-feel-seen/README.md) for controls and architecture.

## Scene saves

Saves tracked vehicle IDs and individual visibility overrides. Reload clears old object references and binds the exact saved vehicles; missing targets are reported.

Scene persistence is registered by the Unscience host through `ISaveParticipantSource`; standalone development hosts retain their existing lifecycle. Use ordinary KSA Save/Load. See [save behavior](../unscience/README.md#scene-saves) and the [design plan](../plans/SAVES.md).

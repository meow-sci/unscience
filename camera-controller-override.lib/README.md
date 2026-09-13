# camera-controller-override.lib

Feature implementation used by Unscience and the development host. See [feature documentation](../camera-controller-override/README.md) for controls and architecture.

## Scene saves

Saves the complete authored keyframe list, nested animation groups, pending group and return-to-start settings. Restores stopped for deliberate replay from the loaded native camera pose. Captured transforms, playback progress, target delegates and unscheduled numeric form drafts are not serialized. Custom API animation types/delegates produce a capture warning rather than invalid JSON.

Scene persistence is registered by the Unscience host through `ISaveParticipantSource`; standalone development hosts retain their existing lifecycle. Use ordinary KSA Save/Load. See [save behavior](../unscience/README.md#scene-saves) and the [design plan](../plans/SAVES.md).

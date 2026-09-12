# Bloomin' Onion library

Runtime planetary ring authoring for the Unscience panel and legacy standalone host.
`RingDefinition` holds detached geometry, painted bands/stripes/noise, volumetric dust,
rock-field LODs and materials. `RingDefinitionController` owns applied definitions and
original template references; `RingReferenceBuilder` resolves assets and constructs the
game ring tree, then `RingRendererRebuilder` recreates native render resources.

Use the panel to choose a body, edit a ring, and Apply; Remove restores the captured
original. Named presets remain in `.unscience/bloomin-onion-rings.toml`. See the
[host usage](../bloomin-onion/README.md) for the complete editor controls.

## Native saves

Normal KSA saves retain exact body assignments and complete applied definitions, including
painted textures as recipes. Unapplied editor changes are excluded. Load first restores
all old celestial template references and clears baseline ownership, then rebuilds saved
rings on exact matching bodies. Missing assets/bodies or unsupported painted-band support
warn and preserve the original saved feature payload. Stock/custom GPU assets are never
serialized. Rocky overlays restore after ring creation and reset before it.

Repeated loads must preserve Remove's original-ring baseline without leaking prior-world
references. Renderer rebuilds can briefly hitch and require native acceptance; managed
round-trip checks alone do not validate Vulkan resource lifetime. See
[integration scope](../scope/rings.md) and [world save checks](../world-saves.tests/README.md).

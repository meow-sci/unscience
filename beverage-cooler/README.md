# Beverage Cooler

An asset-only part bundled with Unscience. Find **Beverage Cooler** under **Unscience Supplies**
or **Life Support**/**Structural** in the vehicle editor. The native variant control chooses **Red**
(`Unscience_Cooler_Red`) or **Stainless Steel** (`Unscience_Cooler_StainlessSteel`). Both use the
same mass, mounting, supplies and animation; KSA displays the native variant IDs in its selector.
Use the part's native **Actuate** slider to open the lid. It turns 100° around the rear hinge over
1.2 seconds, with a matching moving collider. The cooler surface-mounts from its flat base.

The roughly 65 × 40 × 46 cm cooler weighs 5.5 kg empty. Its 30 L native supplies locker holds
**12 meals and 2 CO₂ scrubber canisters**, adding 7.8 kg when full. KSA controls loading,
consumption and transfers. Crew access follows native habitat passage rules: both ends and any
intervening parts must support the passage. An arbitrary surface attachment does not guarantee
access from a cabin. This is native food/scrubber storage; beverage-specific consumption, cooling,
water and oxygen tanks, crew seating and pressurization are not provided.

Native transfers within a shared habitat zone require an able crew carrier. With life support
enabled, a healthy EVA kitten with suitable cargo space can retrieve supplies within 2 metres of
the locker, even when the locker is outside the cabin's habitat zone.

KSA saves the selected variant, remaining supplies and fractional consumption, lid position and
target, and part placement in its normal scene saves. Blueprints follow KSA's native resource policy.
No Unscience sidecar record or runtime
feature host is needed. Keep the part IDs, module keys and ordered `Body`, `Lid`, `LidCollider`
placements stable for existing craft. Removing the assets makes their craft dependency unavailable.
Native save/load and blueprint acceptance are listed in [the save plan](../plans/saves-acceptance.md#beverage-cooler).

The shell and lid use custom GLB meshes and three diffuse/normal/ORM material sets; editable Blender
source, previews and rebuild instructions live in [Art](Art/README.md). Only XML, runtime meshes,
animation and PNG textures ship. Both visual variants share the same collider-only lid subpart;
their differing body/lid subparts contain only rendering and picking modules, as KSA's cosmetic
variant validator requires. [Integration details](../scope/beverage-cooler.md).

Restart KSA after installing the lid/finish fix to clear its process-wide model cache. The update
keeps existing cooler part IDs, supplies and saved lid state. In-game retesting remains pending.

The [native XML test harness](../wing-walker.native-tests/README.md) checks inheritance, the native
variant validator, supplies capacities and native module-data serialization. Renderer, physical
contacts and a complete in-game save/load still need native acceptance.

"""Validate the exported Wing Walker chair's geometry, foil UVs and texture budgets."""
import argparse
import json
import math
from pathlib import Path
import runpy
import struct
import sys
import xml.etree.ElementTree as ET

# Reuse the small standard-library GLB reader; neither checker runs on import.
_helpers = runpy.run_path(str(Path(__file__).with_name("check-cooler-assets.py")))
require = _helpers["require"]
read_glb = _helpers["read_glb"]
accessor_values = _helpers["accessor_values"]
identity_node = _helpers["identity_node"]

CHAIR = "Unscience_WingWalker_BeachChair"
VISIBLE_NAMES = {CHAIR, CHAIR + "_Frame"}
MESH_NAMES = VISIBLE_NAMES | {CHAIR + "_VM"}
TEXTURE_NAMES = {f"WingWalker_BeachChair_{kind}.png" for kind in ("Diffuse", "Normal", "ORM")}
VISIBLE_TRIANGLES_MAX = 1200
VISIBLE_VERTICES_MAX = 3000
PICKING_TRIANGLES_MAX = 150
PICKING_VERTICES_MAX = 256
TEXTURE_EDGE_MAX = 256
FOIL_UV_PER_METRE = 0.171


def check_frame(positions, normals, uvs, indices):
    require(all(0.495 - 1e-5 <= u <= 0.605 + 1e-5 and 0.39 - 1e-5 <= v <= 0.49 + 1e-5 for u, v in uvs),
            "Frame UVs left the native Core wrinkled-foil rectangle")
    densities = []
    for offset in range(0, len(indices), 3):
        a, b, c = indices[offset:offset + 3]
        edge1 = [positions[b][axis] - positions[a][axis] for axis in range(3)]
        edge2 = [positions[c][axis] - positions[a][axis] for axis in range(3)]
        cross = [edge1[1] * edge2[2] - edge1[2] * edge2[1], edge1[2] * edge2[0] - edge1[0] * edge2[2],
                 edge1[0] * edge2[1] - edge1[1] * edge2[0]]
        length = math.sqrt(sum(value * value for value in cross))
        require(length > 1e-10, "Frame contains a degenerate triangle")
        face = [value / length for value in cross]
        for index in (a, b, c):
            require(sum((normals[index][axis] - normals[a][axis]) ** 2 for axis in range(3)) < 1e-9,
                    "Frame normals are smoothed across a triangle rather than flat")
            require(sum(face[axis] * normals[index][axis] for axis in range(3)) > 0.9995,
                    "Frame normal does not match its planar face")
        for first, second in ((a, b), (b, c), (c, a)):
            world_length = math.sqrt(sum((positions[first][axis] - positions[second][axis]) ** 2 for axis in range(3)))
            uv_length = math.sqrt(sum((uvs[first][axis] - uvs[second][axis]) ** 2 for axis in range(2)))
            density = uv_length / world_length
            # Mitered ends shear slightly; reject stretched full-patch UVs while allowing those corners.
            require(FOIL_UV_PER_METRE * 0.75 <= density <= FOIL_UV_PER_METRE * 1.25,
                    "Frame UV density/aspect left the native-scale band around 0.171 UV/metre")
            densities.append(density)
    return {"uv_density_min": min(densities), "uv_density_max": max(densities)}


def check_chair_assets(root):
    root = Path(root)
    xml = ET.parse(root / "WingWalker.xml").getroot()
    chair = xml.find("SubPart[@Id='Unscience_WingWalker_Chair']")
    require(chair is not None, "Missing stable native Chair subpart")
    model_rows = chair.findall("Components/PartModel")
    require(len(model_rows) == 2, "Expected exactly two native chair render models")
    models = {model.attrib["Key"]: (model.find("Mesh").attrib["Ref"], model.find("Material").attrib["Ref"])
              for model in model_rows}
    require(models == {"ChairModel": (CHAIR, "Unscience_WingWalker_BeachChairMaterial"),
                       "ChairFrameModel": (CHAIR + "_Frame", "CoreSurfacesB_Material")},
            "Native chair model keys, meshes or external material bindings changed")
    require(chair.find("Components/MeshView/Mesh").attrib["Ref"] == CHAIR + "_VM", "Native chair picking reference changed")
    require({path.name for path in (root / "Textures").glob("WingWalker_BeachChair_*.png")} == TEXTURE_NAMES,
            "Expected exactly three external chair PNG maps")
    require({Path(texture.attrib["Path"]).name for texture in xml.findall("Texture")
             if texture.attrib.get("Id", "").startswith("Unscience_WingWalker_BeachChair")} == TEXTURE_NAMES,
            "Native XML must reference the three stable external chair PNG names")
    document, binary, byte_count = read_glb(root / "Meshes" / "WingWalker_BeachChair.glb")
    meshes = document.get("meshes", [])
    require(len(meshes) == 3 and {mesh.get("name") for mesh in meshes} == MESH_NAMES, "The three native chair mesh names must remain stable")
    require(not any(document.get(key) for key in ("animations", "skins", "materials", "images", "textures")),
            "Chair GLB must be geometry-only: no embedded images or materials; XML supplies native materials")
    nodes = document.get("nodes", [])
    require(len(nodes) == 3 and sorted(node.get("mesh", -1) for node in nodes) == list(range(3)), "Expected one node per chair mesh")
    require(all(identity_node(node) and not node.get("children") for node in nodes), "Chair mesh nodes must have baked identity transforms")
    require(sorted(document["scenes"][document.get("scene", 0)].get("nodes", [])) == list(range(3)), "Default scene must expose all three meshes once")
    result = {"file_bytes": byte_count, "meshes": {}, "visible_triangles": 0, "visible_vertices": 0,
              "picking_triangles": 0, "picking_vertices": 0, "textures": {}}
    used_accessors = set()
    for mesh in meshes:
        name = mesh["name"]
        require(len(mesh["primitives"]) == 1, f"{name}: expected one primitive")
        primitive = mesh["primitives"][0]
        require(primitive.get("mode", 4) == 4 and "indices" in primitive and not primitive.get("targets"), f"{name}: expected indexed triangles")
        require("material" not in primitive, f"{name}: native XML owns the material binding")
        attributes = primitive["attributes"]
        visible = name in VISIBLE_NAMES
        required = {"POSITION", "NORMAL", "TEXCOORD_0", "TANGENT"} if visible else {"POSITION", "NORMAL"}
        require(required <= attributes.keys(), f"{name}: missing native vertex attributes")
        streams = {attribute: accessor_values(document, binary, index) for attribute, index in attributes.items()}
        positions = streams["POSITION"]
        vertices = len(positions)
        for attribute, index in attributes.items():
            require(len(streams[attribute]) == vertices, f"{name}: {attribute} count differs from POSITION")
            if attribute in required:
                accessor = document["accessors"][index]
                expected_type = {"POSITION": "VEC3", "NORMAL": "VEC3", "TEXCOORD_0": "VEC2", "TANGENT": "VEC4"}[attribute]
                require(accessor["type"] == expected_type and accessor["componentType"] == 5126, f"{name}: expected float {attribute} stream")
        index_accessor = document["accessors"][primitive["indices"]]
        require(index_accessor["type"] == "SCALAR" and index_accessor["componentType"] in (5121, 5123, 5125), f"{name}: expected unsigned scalar indices")
        indices = [value[0] for value in accessor_values(document, binary, primitive["indices"])]
        require(len(indices) % 3 == 0 and max(indices) < vertices, f"{name}: incomplete triangles or indices out of range")
        triangles = len(indices) // 3
        bounds = {"min": [min(position[axis] for position in positions) for axis in range(3)],
                  "max": [max(position[axis] for position in positions) for axis in range(3)]}
        result["meshes"][name] = {"triangles": triangles, "vertices": vertices, "bounds": bounds}
        group = "visible" if visible else "picking"
        result[group + "_triangles"] += triangles
        result[group + "_vertices"] += vertices
        if name == CHAIR + "_Frame":
            result["foil"] = check_frame(positions, streams["NORMAL"], streams["TEXCOORD_0"], indices)
        used_accessors.update(attributes.values())
        used_accessors.add(primitive["indices"])
    require(used_accessors == set(range(len(document["accessors"]))), "Chair export retains unreferenced accessor payloads")
    proxy = result["meshes"][CHAIR + "_VM"]["bounds"]
    visible_bounds = [result["meshes"][name]["bounds"] for name in VISIBLE_NAMES]
    for axis in range(3):
        require(proxy["min"][axis] <= min(bounds["min"][axis] for bounds in visible_bounds) + 1e-5
                and proxy["max"][axis] >= max(bounds["max"][axis] for bounds in visible_bounds) - 1e-5,
                f"Chair picking bounds do not enclose visible geometry on local axis {axis}")
    for group, triangles_max, vertices_max in (("visible", VISIBLE_TRIANGLES_MAX, VISIBLE_VERTICES_MAX),
                                               ("picking", PICKING_TRIANGLES_MAX, PICKING_VERTICES_MAX)):
        require(result[group + "_triangles"] <= triangles_max, f"{group}: {result[group + '_triangles']} triangles exceeds {triangles_max}")
        require(result[group + "_vertices"] <= vertices_max, f"{group}: {result[group + '_vertices']} exported vertices exceeds {vertices_max}")
    for name in sorted(TEXTURE_NAMES):
        data = (root / "Textures" / name).read_bytes()
        require(data[:8] == b"\x89PNG\r\n\x1a\n" and data[12:16] == b"IHDR" and len(data) >= 33, f"{name}: expected PNG IHDR")
        width, height = struct.unpack_from(">II", data, 16)
        require(0 < width <= TEXTURE_EDGE_MAX and 0 < height <= TEXTURE_EDGE_MAX, f"{name}: {width}x{height} exceeds {TEXTURE_EDGE_MAX}px edge budget")
        result["textures"][name] = {"width": width, "height": height, "file_bytes": len(data)}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", type=Path, default=Path(__file__).resolve().parent.parent / "wing-walker")
    parser.add_argument("--json", action="store_true", help="report per-mesh and texture measurements")
    args = parser.parse_args()
    try:
        result = check_chair_assets(args.root)
    except (ValueError, KeyError, IndexError, AttributeError, OSError, ET.ParseError, struct.error) as error:
        print(f"FAIL: chair assets: {error}", file=sys.stderr)
        return 1
    if args.json:
        print(json.dumps(result, indent=2))
    else:
        print(f"PASS: chair assets: {result['visible_triangles']} visible triangles/{result['visible_vertices']} vertices, "
              f"{result['picking_triangles']} picking triangles/{result['picking_vertices']} vertices, "
              "flat frame normals, native foil UVs, three external PNGs <= 256px")
    return 0


if __name__ == "__main__":
    sys.exit(main())

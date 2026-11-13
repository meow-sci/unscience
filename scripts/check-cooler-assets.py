"""Check exported cooler geometry, picking and texture budgets using only the standard library."""
import argparse
import json
import math
from pathlib import Path
import runpy
import struct
import sys
import xml.etree.ElementTree as ET

VISIBLE_TRIANGLES_MAX = 1000
VISIBLE_VERTICES_MAX = 2000
PICKING_TRIANGLES_MAX = 120
PICKING_VERTICES_MAX = 192
TEXTURE_EDGE_MAX = 128
VISIBLE_NAMES = {f"Unscience_Cooler_{part}{surface}" for part in ("Body", "Lid") for surface in ("Shell", "Trim", "Foil")}
PICKING_NAMES = {"Unscience_Cooler_Body_VM", "Unscience_Cooler_Lid_VM"}
MODEL_KEYS = {f"Unscience_Cooler_{finish}{part}{surface}" for finish in ("Red", "Steel")
              for part in ("Body", "Lid") for surface in ("Shell", "Trim", "Foil")}
TEXTURE_NAMES = {f"BeverageCooler_{finish}_{kind}.png" for finish in ("Red", "Steel", "Common")
                 for kind in ("Diffuse", "Normal", "ORM")}
COMPONENTS = {5121: ("B", 1), 5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}
WIDTHS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read_glb(path):
    data = path.read_bytes()
    require(len(data) >= 20, "GLB header is truncated")
    magic, version, length = struct.unpack_from("<4sII", data)
    require(magic == b"glTF" and version == 2 and length == len(data), "Expected a complete GLB 2.0 file")
    chunks = []
    offset = 12
    while offset < len(data):
        require(offset + 8 <= len(data), "GLB chunk header is truncated")
        size, kind = struct.unpack_from("<II", data, offset)
        require(size % 4 == 0 and offset + 8 + size <= len(data), "GLB chunk bounds/alignment are invalid")
        chunks.append((kind, data[offset + 8:offset + 8 + size]))
        offset += 8 + size
    require([kind for kind, _ in chunks] == [0x4E4F534A, 0x004E4942], "Expected one embedded JSON/BIN chunk pair")
    document = json.loads(chunks[0][1])
    binary = chunks[1][1]
    require(len(document.get("buffers", [])) == 1 and "uri" not in document["buffers"][0], "Mesh data must be embedded in one buffer")
    require(0 <= len(binary) - document["buffers"][0]["byteLength"] <= 3, "GLB buffer length does not match its payload")
    return document, binary, len(data)


def accessor_values(document, binary, index):
    accessor = document["accessors"][index]
    require("sparse" not in accessor, "Sparse accessors are outside the compact cooler export contract")
    require(accessor["componentType"] in COMPONENTS and accessor["type"] in WIDTHS, "Unsupported accessor representation")
    code, byte_size = COMPONENTS[accessor["componentType"]]
    width = WIDTHS[accessor["type"]]
    view = document["bufferViews"][accessor["bufferView"]]
    require(view.get("buffer", 0) == 0, "Accessor points outside the embedded buffer")
    start = accessor.get("byteOffset", 0)
    stride = view.get("byteStride", byte_size * width)
    count = accessor["count"]
    require(count > 0 and stride >= byte_size * width, "Accessor count or stride is invalid")
    require(start >= 0 and start + (count - 1) * stride + byte_size * width <= view["byteLength"], "Accessor exceeds its buffer view")
    offset = view.get("byteOffset", 0)
    require(offset >= 0 and offset + view["byteLength"] <= len(binary), "Buffer view exceeds the BIN chunk")
    values = [struct.unpack_from("<" + code * width, binary, offset + start + item * stride) for item in range(count)]
    require(all(math.isfinite(component) for value in values for component in value), "Accessor contains non-finite values")
    return values


def identity_node(node):
    expected = {"translation": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1],
                "matrix": [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]}
    return all(len(node.get(key, value)) == len(value)
               and all(abs(actual - wanted) < 1e-7 for actual, wanted in zip(node.get(key, value), value))
               for key, value in expected.items())


def check_geometry(path):
    document, binary, byte_count = read_glb(path)
    meshes = document.get("meshes", [])
    require(len(meshes) == 8 and {mesh.get("name") for mesh in meshes} == VISIBLE_NAMES | PICKING_NAMES,
            "Expected six stable visible mesh names and two stable picking mesh names")
    require(not any(document.get(key) for key in ("animations", "skins", "materials", "images", "textures")),
            "The model GLB must contain geometry only; XML owns materials and animation is separate")
    nodes = document.get("nodes", [])
    require(len(nodes) == 8 and sorted(node.get("mesh", -1) for node in nodes) == list(range(8)),
            "Each of the eight meshes must have one node")
    require(all(identity_node(node) and not node.get("children") for node in nodes), "Mesh nodes must have baked identity transforms")
    scene = document["scenes"][document.get("scene", 0)]
    require(sorted(scene.get("nodes", [])) == list(range(8)), "Default scene must expose all eight mesh nodes once")
    result = {"file_bytes": byte_count, "meshes": {}, "visible_triangles": 0, "visible_vertices": 0,
              "picking_triangles": 0, "picking_vertices": 0}
    used_accessors = set()
    geometry = {}
    for mesh in meshes:
        name = mesh["name"]
        require(len(mesh["primitives"]) == 1, f"{name}: expected one primitive")
        primitive = mesh["primitives"][0]
        require(primitive.get("mode", 4) == 4 and "indices" in primitive and not primitive.get("targets"), f"{name}: expected indexed triangles without morph targets")
        attributes = primitive["attributes"]
        visible = name in VISIBLE_NAMES
        required = {"POSITION", "NORMAL", "TEXCOORD_0", "TANGENT"} if visible else {"POSITION", "NORMAL"}
        require(required <= attributes.keys(), f"{name}: missing native-visible vertex attributes")
        position_accessor = document["accessors"][attributes["POSITION"]]
        require(position_accessor["type"] == "VEC3" and position_accessor["componentType"] == 5126, f"{name}: expected float3 positions")
        positions = accessor_values(document, binary, attributes["POSITION"])
        vertices = len(positions)
        for attribute, accessor_index in attributes.items():
            require(len(accessor_values(document, binary, accessor_index)) == vertices, f"{name}: {attribute} count differs from POSITION")
            if attribute in required:
                expected_type = {"POSITION": "VEC3", "NORMAL": "VEC3", "TEXCOORD_0": "VEC2", "TANGENT": "VEC4"}[attribute]
                accessor = document["accessors"][accessor_index]
                require(accessor["type"] == expected_type and accessor["componentType"] == 5126, f"{name}: {attribute} representation changed")
        index_accessor = document["accessors"][primitive["indices"]]
        require(index_accessor["type"] == "SCALAR" and index_accessor["componentType"] in (5121, 5123, 5125), f"{name}: expected unsigned scalar indices")
        indices = [value[0] for value in accessor_values(document, binary, primitive["indices"])]
        require(len(indices) % 3 == 0 and max(indices) < vertices, f"{name}: incomplete triangles or out-of-range indices")
        triangles = len(indices) // 3
        bounds = {"min": [min(position[axis] for position in positions) for axis in range(3)],
                  "max": [max(position[axis] for position in positions) for axis in range(3)]}
        result["meshes"][name] = {"triangles": triangles, "vertices": vertices, "bounds": bounds}
        if visible:
            geometry[name] = {"positions": positions, "indices": indices,
                              "normals": accessor_values(document, binary, attributes["NORMAL"])}
        if name.endswith("Foil"):
            uvs = accessor_values(document, binary, attributes["TEXCOORD_0"])
            require(all(0.495 - 1e-5 <= u <= 0.605 + 1e-5 and 0.39 - 1e-5 <= v <= 0.49 + 1e-5 for u, v in uvs),
                    f"{name}: UVs left the native wrinkled-foil patch")
        group = "visible" if visible else "picking"
        result[group + "_triangles"] += triangles
        result[group + "_vertices"] += vertices
        used_accessors.update(attributes.values())
        used_accessors.add(primitive["indices"])
    require(used_accessors == set(range(len(document["accessors"]))), "Model GLB retains unreferenced accessor payloads")
    for part in ("Body", "Lid"):
        proxy = result["meshes"][f"Unscience_Cooler_{part}_VM"]["bounds"]
        visible = [result["meshes"][f"Unscience_Cooler_{part}{surface}"]["bounds"] for surface in ("Shell", "Trim", "Foil")]
        for axis in range(3):
            require(proxy["min"][axis] <= min(bounds["min"][axis] for bounds in visible) + 1e-5
                    and proxy["max"][axis] >= max(bounds["max"][axis] for bounds in visible) - 1e-5,
                    f"{part}: picking proxy bounds do not enclose visible geometry on local axis {axis}")
    coverage = runpy.run_path(str(Path(__file__).with_name("check-cooler-surfaces.py")))
    result["surface_coverage"] = coverage["check_surfaces"](geometry)
    return result


def check_assets(root):
    root = Path(root)
    xml = ET.parse(root / "BeverageCooler.xml").getroot()
    model_keys = [model.attrib.get("Key") for model in xml.findall(".//PartModel")]
    require(len(model_keys) == 12 and set(model_keys) == MODEL_KEYS, "The twelve native PartModel cache keys must remain unique and stable")
    for model in xml.findall(".//PartModel"):
        if model.attrib["Key"].endswith("Foil"):
            require(model.find("Material").attrib["Ref"] == "CoreSurfacesB_Material",
                    "Cooler lining must reference the native foil material without a copied payload")
    referenced_textures = {Path(texture.attrib["Path"]).name for texture in xml.findall("Texture")}
    require(referenced_textures == TEXTURE_NAMES, "XML must reference the nine stable cooler PNG names")
    require({path.name for path in (root / "Textures").glob("BeverageCooler_*.png")} == TEXTURE_NAMES, "Missing or extra cooler textures")
    result = check_geometry(root / "Meshes" / "BeverageCooler.glb")
    for group, triangles_max, vertices_max in (("visible", VISIBLE_TRIANGLES_MAX, VISIBLE_VERTICES_MAX),
                                               ("picking", PICKING_TRIANGLES_MAX, PICKING_VERTICES_MAX)):
        triangles, vertices = result[group + "_triangles"], result[group + "_vertices"]
        require(triangles <= triangles_max, f"{group}: {triangles} triangles exceeds budget {triangles_max}")
        require(vertices <= vertices_max, f"{group}: {vertices} exported vertices exceeds budget {vertices_max}")
    result["textures"] = {}
    for name in sorted(TEXTURE_NAMES):
        data = (root / "Textures" / name).read_bytes()
        require(data[:8] == b"\x89PNG\r\n\x1a\n" and data[12:16] == b"IHDR" and len(data) >= 33, f"{name}: expected PNG IHDR")
        width, height = struct.unpack_from(">II", data, 16)
        require(0 < width <= TEXTURE_EDGE_MAX and 0 < height <= TEXTURE_EDGE_MAX, f"{name}: {width}x{height} exceeds {TEXTURE_EDGE_MAX}px edge budget")
        result["textures"][name] = {"width": width, "height": height, "file_bytes": len(data)}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", type=Path, default=Path(__file__).resolve().parent.parent / "beverage-cooler",
                        help="asset root, or built unscience package directory")
    parser.add_argument("--json", action="store_true", help="report per-mesh and texture measurements")
    args = parser.parse_args()
    try:
        result = check_assets(args.root)
    except (ValueError, KeyError, IndexError, OSError, ET.ParseError, struct.error) as error:
        print(f"FAIL: cooler assets: {error}", file=sys.stderr)
        return 1
    if args.json:
        print(json.dumps(result, indent=2))
    else:
        print(f"PASS: cooler assets: {result['visible_triangles']} visible triangles/{result['visible_vertices']} vertices, "
              f"{result['picking_triangles']} picking triangles/{result['picking_vertices']} vertices, "
              f"eight single-primitive meshes, one-sided surface coverage, nine PNGs <= {TEXTURE_EDGE_MAX}px")
    return 0


if __name__ == "__main__":
    sys.exit(main())

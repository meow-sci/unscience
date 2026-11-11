"""Run in Blender to rebuild the simple leather Standing Mount asset.

Authoring frame: metres, +Z up, -Y forward. Exported vertices use KSA's
+X forward, -Z up frame at the existing standing crew origin. No Core art
is included. Execute through Blender's Text Editor or the Blender connection.
"""
from pathlib import Path
import math

import bpy
import bmesh
import numpy as np
from mathutils import Matrix


ROOT = Path(__file__).resolve().parents[1]
NAME = "Unscience_WingWalker_FootStraps"
COLLECTION = "WingWalker_StandingMount"
GROUND = 0.2901232


def image(name, pixels, data=False):
    for old in list(bpy.data.images):
        if old.name == name or old.name.startswith(name + '.'):
            bpy.data.images.remove(old)
    height, width, _ = pixels.shape
    result = bpy.data.images.new(name, width=width, height=height, alpha=True)
    # Pixels below are PNG channel values; avoid a second linear-to-sRGB encode.
    result.colorspace_settings.name = "Non-Color"
    result.pixels.foreach_set(pixels.astype(np.float32).ravel())
    result.filepath_raw = str(ROOT / "Textures" / (name + ".png"))
    formats = bpy.types.ImageFormatSettings.bl_rna.properties['file_format'].enum_items
    result.file_format = next(item.identifier for item in formats if item.identifier == "PNG")
    result.save()
    path = result.filepath_raw
    bpy.data.images.remove(result)
    loaded = bpy.data.images.load(path, check_existing=False)
    loaded.name = name
    loaded.colorspace_settings.name = "Non-Color" if data else "sRGB"
    return loaded


def leather():
    size = 256
    rng = np.random.default_rng(741)
    y, x = np.mgrid[0:size, 0:size] / size
    grain = rng.normal(0, 0.018, (size, size))
    grain += 0.018 * np.sin(x * 97 + np.sin(y * 61))
    grain += 0.012 * np.sin(y * 137 + np.sin(x * 53))
    color = np.ones((size, size, 4))
    # Warm brown, with slightly darker burnished edges and quiet tan stitching.
    edge = np.minimum(y, 1 - y)
    shade = 1 + grain - 0.25 * np.exp(-edge * 55)
    color[:, :, :3] = np.array([0.36, 0.185, 0.085]) * shade[:, :, None]
    stitches = ((abs(y - 0.085) < 0.005) | (abs(y - 0.915) < 0.005))
    stitches &= (x * 36 % 1 > 0.22) & (x * 36 % 1 < 0.72)
    color[stitches, :3] = [0.61, 0.43, 0.25]
    diffuse = image("WingWalker_Leather_Diffuse", color)
    normal = np.ones((size, size, 4))
    normal[:, :, 0] = 0.5 + grain * 0.6
    normal[:, :, 1] = 0.5 + np.roll(grain, 1, axis=0) * 0.6
    normal[:, :, 2] = 1
    normal = image("WingWalker_Leather_Normal", normal, True)
    orm = np.ones((size, size, 4))
    orm[:, :, 1] = np.clip(0.82 + grain, 0, 1)
    orm[:, :, 2] = 0
    orm = image("WingWalker_Leather_ORM", orm, True)
    mat = bpy.data.materials.get("WingWalker_BrownLeather") or bpy.data.materials.new("WingWalker_BrownLeather")
    mat.use_nodes = True
    mat.diffuse_color = (0.107, 0.029, 0.008, 1)
    nodes = mat.node_tree.nodes
    nodes.clear()
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    shader.inputs['Roughness'].default_value = 0.82
    output = nodes.new("ShaderNodeOutputMaterial")
    mat.node_tree.links.new(shader.outputs['BSDF'], output.inputs['Surface'])
    tex = nodes.new("ShaderNodeTexImage"); tex.image = diffuse
    mat.node_tree.links.new(tex.outputs['Color'], shader.inputs['Base Color'])
    tex = nodes.new("ShaderNodeTexImage"); tex.image = normal
    bump = nodes.new("ShaderNodeNormalMap")
    mat.node_tree.links.new(tex.outputs['Color'], bump.inputs['Color'])
    mat.node_tree.links.new(bump.outputs['Normal'], shader.inputs['Normal'])
    tex = nodes.new("ShaderNodeTexImage"); tex.image = orm
    separate = nodes.new("ShaderNodeSeparateColor")
    mat.node_tree.links.new(tex.outputs['Color'], separate.inputs['Color'])
    mat.node_tree.links.new(separate.outputs['Green'], shader.inputs['Roughness'])
    return mat


def build():
    old = bpy.data.collections.get(COLLECTION)
    if old:
        for obj in list(old.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(old)
    for mesh in list(bpy.data.meshes):
        if mesh.name.startswith(NAME) and mesh.users == 0:
            bpy.data.meshes.remove(mesh)
    collection = bpy.data.collections.new(COLLECTION)
    bpy.context.scene.collection.children.link(collection)
    material = leather()
    vertices, faces, uvs = [], [], []
    # A broad U over each boot, ending in small flat leather attachment tabs.
    profile = [(-0.132, 0.006), (-0.112, 0.006)]
    profile += [(-0.105 * math.cos(t), 0.012 + 0.098 * math.sin(t))
                for t in np.linspace(0, math.pi, 25)]
    profile += [(0.112, 0.006), (0.132, 0.006)]
    length = len(profile)
    for center in (-0.13, 0.13):
        start = len(vertices)
        for px, pz in profile:
            for py, dz in [(-0.068, 0), (0.014, 0), (-0.068, -0.006), (0.014, -0.006)]:
                vertices.append((center + px, py, pz + dz))
        for i in range(length - 1):
            a, b = start + i * 4, start + (i + 1) * 4
            u0, u1 = i / (length - 1), (i + 1) / (length - 1)
            for corners in [(a, b, b+1, a+1), (a+2, a+3, b+3, b+2),
                            (a, a+2, b+2, b), (a+1, b+1, b+3, a+3)]:
                faces.append(corners)
                uvs.append(((u0, 0), (u1, 0), (u1, 1), (u0, 1)))
        for i in (0, length - 1):
            a = start + i * 4
            faces.append((a, a+1, a+3, a+2))
            uvs.append(((0, 0), (1, 0), (1, 1), (0, 1)))
    mesh = bpy.data.meshes.new(NAME)
    mesh.from_pydata(vertices, [], faces); mesh.update()
    uv = mesh.uv_layers.new(name="UVMap")
    for polygon, coordinates in zip(mesh.polygons, uvs):
        for index, coordinate in zip(polygon.loop_indices, coordinates):
            uv.data[index].uv = coordinate
    bm = bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(NAME, mesh)
    collection.objects.link(obj); mesh.materials.append(material)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True); bpy.context.view_layer.objects.active = obj
    bevel = obj.modifiers.new('Soft leather edges', 'BEVEL')
    bevel.width = 0.0012; bevel.segments = 2
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    obj['description'] = 'Two simple stitched brown leather straps over the standing kitten boots'
    obj['game_ground_z'] = GROUND
    print('Created', obj.name, 'vertices', len(obj.data.vertices), 'polygons', len(obj.data.polygons))
    return obj


def export(obj):
    # KSA ignores GLB node transforms: bake all placement into mesh vertices.
    frame = Matrix(((0, -1, 0, 0.01404), (-1, 0, 0, 0.00495),
                    (0, 0, -1, GROUND), (0, 0, 0, 1)))
    obj.name = NAME + '_authoring'
    obj.data.name = NAME + '_authoring'
    exports = []
    for suffix in ('', '_VM'):
        mesh = obj.data.copy(); mesh.name = NAME + suffix
        mesh.transform(frame)
        clone = bpy.data.objects.new(NAME + suffix, mesh)
        bpy.context.scene.collection.objects.link(clone)
        exports.append(clone)
    bpy.ops.object.select_all(action='DESELECT')
    for clone in exports:
        clone.select_set(True)
    # This exporter uses a dynamic format enum; inspect its available values.
    from io_scene_gltf2 import ExportGLTF2
    base = next(cls for cls in ExportGLTF2.__mro__ if 'export_format' in getattr(cls, '__annotations__', {}))
    formats = base.__annotations__['export_format'].keywords['items'](None, bpy.context)
    binary = next(item[0] for item in formats if '.glb' in item[1].lower())
    bpy.ops.export_scene.gltf(filepath=str(ROOT / 'Meshes' / 'WingWalker_StandingMount.glb'),
        export_format=binary, use_selection=True, export_yup=False,
        export_animations=False, export_skins=False, export_morph=False,
        export_tangents=True, export_materials='EXPORT')
    for clone in exports:
        mesh = clone.data
        bpy.data.objects.remove(clone, do_unlink=True)
        bpy.data.meshes.remove(mesh)
    obj.name = NAME; obj.data.name = NAME
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    print('Exported visible and picking meshes in KSA part space')


if __name__ == '__main__':
    for directory in ('Textures', 'Meshes'):
        (ROOT / directory).mkdir(exist_ok=True)
    mount = build()
    export(mount)

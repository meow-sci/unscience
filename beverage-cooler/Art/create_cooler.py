"""Rebuild original cooler art in Blender; metres, +Z up and -Y front.

Runtime geometry uses KSA (-Y,-X,-Z). Lid vertices are hinge-local; the
separate native keyframe GLB drives the placed Lid, not a skinned mesh.
"""
from pathlib import Path
import math
import runpy

import bpy
import bmesh
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
COLLECTION = 'Unscience_BeverageCooler'
PREFIX = 'Unscience_Cooler_'
HINGE = Vector((0, .19, .409))
FRAME = Matrix(((0, -1, 0, 0), (-1, 0, 0, 0), (0, 0, -1, 0), (0, 0, 0, 1)))


def deselect_all():
    bpy.context.view_layer.update()
    for candidate in bpy.context.view_layer.objects:
        if candidate is not None:
            candidate.select_set(False)


def ring(width, depth, radius, z, segments=2):
    points = []
    for cx, cy, start in [(width/2-radius, depth/2-radius, 0),
            (-width/2+radius, depth/2-radius, 90),
            (-width/2+radius, -depth/2+radius, 180),
            (width/2-radius, -depth/2+radius, 270)]:
        for i in range(segments + 1):
            angle = math.radians(start + i * 90 / segments)
            points.append((cx + radius*math.cos(angle), cy + radius*math.sin(angle), z))
    return points


class Builder:
    def __init__(self, collection, mats):
        self.collection, self.mats = collection, mats
        self.groups = {key: [] for key in ('BodyShell', 'BodyTrim', 'BodyFoil', 'LidShell', 'LidTrim', 'LidFoil')}

    def add(self, obj, group, band=None):
        deselect_all()
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        if obj.type != 'MESH':
            bpy.ops.object.convert(target='MESH')
            obj = bpy.context.object
        for modifier in list(obj.modifiers):
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        for coll in list(obj.users_collection):
            coll.objects.unlink(obj)
        self.collection.objects.link(obj)
        mat = self.mats['Foil' if group.endswith('Foil') else 'Common' if band is not None else 'Red']
        obj.data.materials.clear()
        obj.data.materials.append(mat)
        uv = obj.data.uv_layers.new(name='UVMap') if not obj.data.uv_layers else obj.data.uv_layers[0]
        for poly in obj.data.polygons:
            # Planar UV per dominant normal; common surfaces use a dedicated atlas strip.
            axes = sorted(range(3), key=lambda i: abs(poly.normal[i]))[:2]
            center = poly.center.copy()
            for loop in poly.loop_indices:
                p = obj.data.vertices[obj.data.loops[loop].vertex_index].co
                if group.endswith('Foil'):
                    # Native atlas patch, with near-native isotropic UV scale on each planar face.
                    uv.data[loop].uv = (.55+(p[axes[0]]-center[axes[0]])*.16,
                                       1-(.44+(p[axes[1]]-center[axes[1]])*.16))
                    continue
                u, v = (p[axes[0]] * 1.3 + .5) % 1, (p[axes[1]] * 1.3 + .5) % 1
                uv.data[loop].uv = ((band + .05 + .9*u)/4 if band is not None else u, v)
        self.groups[group].append(obj)
        obj.select_set(False)
        return obj

    def profile(self, name, profiles, group, band=None, caps=True, reverse=False):
        loops = [ring(*p) for p in profiles]
        n = len(loops[0])
        vertices = [p for loop in loops for p in loop]
        faces = [(i*n+j, i*n+(j+1)%n, (i+1)*n+(j+1)%n, (i+1)*n+j)
                 for i in range(len(loops)-1) for j in range(n)]
        cap_start, cap_end = caps if isinstance(caps, tuple) else (caps, caps)
        if cap_start:
            faces.append(tuple(reversed(range(n))))
        if cap_end:
            faces.append(tuple(range((len(loops)-1)*n, len(loops)*n)))
        if reverse:
            faces = [tuple(reversed(face)) for face in faces]
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(vertices, [], faces)
        mesh.update()
        for poly in mesh.polygons:
            poly.use_smooth = abs(poly.normal.z) < .6
        obj = bpy.data.objects.new(name, mesh)
        self.collection.objects.link(obj)
        return self.add(obj, group, band)

    def box(self, name, center, size, bevel, group, band=None):
        bpy.ops.mesh.primitive_cube_add(size=1, location=center)
        obj = bpy.context.object
        obj.name = name
        obj.scale = size
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        if bevel:
            modifier = obj.modifiers.new('Soft manufactured edges', 'BEVEL')
            modifier.width, modifier.segments = bevel, 1
            modifier = obj.modifiers.new('Corner normals', 'WEIGHTED_NORMAL')
        return self.add(obj, group, band)

    def tube(self, name, points, radius, group, band, closed=False):
        curve = bpy.data.curves.new(name, 'CURVE')
        curve.dimensions, curve.resolution_u = '3D', 2
        curve.bevel_depth, curve.bevel_resolution = radius, 0
        spline = curve.splines.new('POLY')
        spline.points.add(len(points)-1)
        for p, value in zip(spline.points, points):
            p.co = (*value, 1)
        spline.use_cyclic_u = closed
        obj = bpy.data.objects.new(name, curve)
        self.collection.objects.link(obj)
        return self.add(obj, group, band)

    def cylinder(self, name, center, radius, depth, axis, group, band):
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=radius, depth=depth, location=center)
        obj = bpy.context.object
        obj.name = name
        obj.rotation_euler = Vector((0, 0, 1)).rotation_difference(Vector(axis)).to_euler()
        return self.add(obj, group, band)

    def finish(self):
        result = {}
        for key, objects in self.groups.items():
            deselect_all()
            for obj in objects:
                obj.select_set(True)
            bpy.context.view_layer.objects.active = objects[0]
            if len(objects) > 1:
                bpy.ops.object.join()
            obj = bpy.context.object
            obj.name = obj.data.name = PREFIX + key
            bm = bmesh.new()
            bm.from_mesh(obj.data)
            # Profiles carry explicit semantic winding. Recalculating open sheets can face
            # an underside away from the viewer; Blender's two-sided default hides that error.
            bmesh.ops.triangulate(bm, faces=list(bm.faces))
            bm.to_mesh(obj.data)
            bm.free()
            result[key] = obj
        return result


def build():
    old = bpy.data.collections.get(COLLECTION)
    if old:
        for obj in list(old.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(old)
    for mesh in list(bpy.data.meshes):
        if mesh.name.startswith(PREFIX) and mesh.users == 0:
            bpy.data.meshes.remove(mesh)
    coll = bpy.data.collections.new(COLLECTION)
    bpy.context.scene.collection.children.link(coll)
    mats = runpy.run_path(str(ROOT / 'Art/cooler_textures.py'))['materials']()
    mats['Foil'] = runpy.run_path(str(ROOT / '../wing-walker/Art/create_beach_chair.py'))['gold_preview']()
    for mat in mats.values():
        mat.use_backface_culling = True
    b = Builder(coll, mats)
    # Hidden duplicate walls are omitted; the liner and base close the visible shell.
    b.profile('Powder coated insulated shell', [(.65,.40,.045,.083),
        (.65,.40,.045,.400)], 'BodyShell', caps=False)
    b.profile('Rubber protective plinth', [(.614,.364,.035,.0),(.642,.392,.042,.014),
        (.65,.40,.045,.068),(.65,.40,.045,.083)], 'BodyTrim', 1)
    b.profile('Foil-lined insulated tub', [(.65,.40,.045,.400),(.65,.40,.045,.407),
        (.576,.326,.030,.407),(.55,.30,.028,.112)], 'BodyFoil', caps=(False, True))
    # Lid is a separate rigid piece, including its interior and front hand grip.
    b.profile('Rounded lid shell', [(.66,.41,.046,.412),
        (.66,.41,.046,.463),(.602,.352,.029,.463),
        (.591,.341,.025,.456)], 'LidShell', caps=(False, True))
    b.profile('Foil lid underside', [(.66,.41,.046,.412),
        (.565,.315,.030,.412),(.545,.295,.027,.423)], 'LidFoil', caps=(False, True), reverse=True)
    b.profile('Lid gasket', [(.624,.374,.039,.408),
        (.608,.358,.035,.408)], 'LidTrim', 1, False, reverse=True)
    b.box('Front grip', (0,-.208,.439), (.143,.031,.027), .004, 'LidTrim', 1)
    b.box('Latch backing', (0,-.203,.382),(.054,.009,.031),0,'BodyTrim',2)
    b.box('Latch black keeper', (0,-.210,.394),(.026,.006,.009),0,'BodyTrim',3)
    b.cylinder('Latch catch', (0,-.212,.381),.011,.005,(0,-1,0),'BodyTrim',2)
    b.tube('Latch toggle', [(0,-.218,.382),(.025,-.225,.386)],.003,'BodyTrim',2)
    for side in (-1,1):
        x = side*.328
        b.box('Handle mounting plate',(x,0,.303),(.006,.15,.056),0,'BodyTrim',2)
        points = [(side*.337,-.057,.314),(side*.351,-.066,.299),
                  (side*.351,-.058,.239),(side*.351,.058,.239),
                  (side*.351,.066,.299),(side*.337,.057,.314)]
        b.tube('Folding side handle', points,.006,'BodyTrim',2)
        b.tube('Black handle grip',[(side*.351,-.047,.239),(side*.351,.047,.239)],.010,'BodyTrim',1)
    # Front bottle opener: silver backing, dark throat, overhanging fulcrum.
    b.box('Opener backplate',(.224,-.204,.328),(.026,.008,.065),0,'BodyTrim',2)
    b.box('Opener throat',(.224,-.211,.333),(.021,.006,.015),0,'BodyTrim',3)
    b.box('Opener hook',(.224,-.221,.342),(.039,.025,.009),0,'BodyTrim',2)
    # Recognizable hardware only: millimetre screws and individual chain links are omitted.
    for x in (-.19,.19):
        b.box('Rear hinge plate',(x,.202,.390),(.061,.008,.035),0,'BodyTrim',2)
        b.cylinder('Hinge barrel',(x,.204,.410),.009,.072,(1,0,0),'LidTrim',2)
    b.box('Drain recess',(-.324,-.105,.042),(.008,.056,.052),0,'BodyTrim',3)
    b.cylinder('Drain plug',(-.333,-.105,.043),.012,.014,(-1,0,0),'BodyTrim',2)
    result = b.finish()
    hinge = bpy.data.objects.new('Cooler_Hinge_authoring',None)
    coll.objects.link(hinge)
    hinge.location = HINGE
    for key, obj in result.items():
        if key.startswith('Lid'):
            obj.data.transform(Matrix.Translation(-HINGE))
            obj.parent = hinge
    hinge.rotation_euler.x = 0
    hinge.keyframe_insert(data_path='rotation_euler',frame=1)
    hinge.rotation_euler.x = math.radians(-100)
    hinge.keyframe_insert(data_path='rotation_euler',frame=37)
    linear = next(i.identifier for i in bpy.types.Keyframe.bl_rna.properties['interpolation'].enum_items
                  if i.identifier == 'LINEAR')
    for layer in hinge.animation_data.action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for curve in bag.fcurves:
                    for point in curve.keyframe_points:
                        point.interpolation = linear
    bpy.context.scene.frame_set(1)
    return result, hinge, coll


def export(result):
    from io_scene_gltf2 import ExportGLTF2
    base = next(c for c in ExportGLTF2.__mro__ if 'export_format' in getattr(c,'__annotations__',{}))
    formats = base.__annotations__['export_format'].keywords['items'](None,bpy.context)
    binary = next(i[0] for i in formats if '.glb' in i[1].lower())
    clones = []
    for key, original in result.items():
        mesh = original.data.copy()
        mesh.name = PREFIX + key
        mesh.transform(FRAME)
        obj = bpy.data.objects.new(PREFIX + key,mesh)
        # Original mesh has same name; restore the exact atlas identity on the export mesh.
        original.data.name += '_authoring'
        mesh.name = PREFIX + key
        bpy.context.scene.collection.objects.link(obj)
        clones.append(obj)
    proxies = runpy.run_path(str(ROOT / 'Art/cooler_picking.py'))['proxies']
    clones.extend(proxies(FRAME, HINGE, PREFIX, ring))
    deselect_all()
    for obj in clones:
        obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(ROOT/'Meshes/BeverageCooler.glb'),export_format=binary,
        use_selection=True,use_active_scene=True,export_yup=False,export_animations=False,export_skins=False,
        export_morph=False,export_tangents=True,export_materials='NONE')
    for obj in clones:
        mesh=obj.data
        bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.meshes.remove(mesh)


if __name__ == '__main__':
    result, hinge, collection = build()
    export(result)
    print('Cooler built:', {key: len(obj.data.polygons) for key,obj in result.items()})

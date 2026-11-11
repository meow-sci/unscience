"""Build Wing Walker's rainbow webbed beach chair in Blender, in metres.

Authoring: +Z up, -Y forward. Export: baked KSA +X forward, -Z up,
with the existing seated part ground plane at Z=0.3821768.
"""
from pathlib import Path
import math
import runpy

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
NAME = 'Unscience_WingWalker_BeachChair'
COLLECTION = 'WingWalker_BeachChair'
GROUND = 0.3821768
FOIL_U = (.495, .605)
FOIL_V = (.39, .49)
# Share the existing PNG writer, including its color-space handling.
image = runpy.run_path(str(ROOT / 'Art/create_standing_mount.py'))['image']


def material():
    size = 512
    y, x = np.mgrid[0:size, 0:size] / size
    rng = np.random.default_rng(9081)
    fibers = 0.04 * np.sin(x * math.tau * 160) * np.sin(y * math.tau * 192)
    fibers += rng.normal(0, .009, (size, size))
    colors = np.array([[.91, .065, .105], [1, .36, .035], [1, .84, .055],
                       [.08, .69, .20], [.025, .43, .91]])
    stripe = np.clip(((x - .015) / .75 * 5).astype(int), 0, 4)
    diffuse = np.ones((size, size, 4))
    diffuse[:, :, :3] = colors[stripe] * (1 + fibers[:, :, None])
    selvedge = (x < .026) | ((x > .754) & (x < .80))
    diffuse[selvedge, :3] = [.91, .91, .84]
    plastic = x > .80
    stipple = rng.normal(0, .012, (size, size))
    diffuse[plastic, :3] = np.repeat((.89 + stipple[plastic])[:, None], 3, axis=1)
    normal = np.ones((size, size, 4))
    normal[:, :, 0] = .5 + .055 * np.sin(x * math.tau * 160)
    normal[:, :, 1] = .5 + .055 * np.sin(y * math.tau * 192)
    normal[:, :, 2] = 1
    normal[plastic, 0] = .5 + stipple[plastic]
    normal[plastic, 1] = .5 + np.roll(stipple,1,axis=0)[plastic]
    orm = np.ones((size, size, 4))
    orm[:, :, 1] = .84 + fibers * .2; orm[:, :, 2] = 0
    orm[plastic, 1] = .57 + stipple[plastic]; orm[plastic, 2] = 0
    textures = [image('WingWalker_BeachChair_' + suffix, data, linear)
                for suffix, data, linear in [('Diffuse', diffuse, False),
                    ('Normal', normal, True), ('ORM', orm, True)]]
    mat = bpy.data.materials.get(NAME) or bpy.data.materials.new(NAME)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes; nodes.clear(); links = mat.node_tree.links
    shader = nodes.new('ShaderNodeBsdfPrincipled')
    output = nodes.new('ShaderNodeOutputMaterial')
    links.new(shader.outputs['BSDF'], output.inputs['Surface'])
    for index, img in enumerate(textures):
        tex = nodes.new('ShaderNodeTexImage'); tex.image = img
        if index == 0:
            links.new(tex.outputs['Color'], shader.inputs['Base Color'])
        elif index == 1:
            node = nodes.new('ShaderNodeNormalMap')
            links.new(tex.outputs['Color'], node.inputs['Color'])
            links.new(node.outputs['Normal'], shader.inputs['Normal'])
        else:
            node = nodes.new('ShaderNodeSeparateColor')
            links.new(tex.outputs['Color'], node.inputs['Color'])
            links.new(node.outputs['Green'], shader.inputs['Roughness'])
            links.new(node.outputs['Blue'], shader.inputs['Metallic'])
    return mat


def gold_preview():
    # Portable authoring proxy only. Runtime uses the game's CoreSurfacesB_Material.
    mat = bpy.data.materials.get(NAME+'_GoldPreview') or bpy.data.materials.new(NAME+'_GoldPreview')
    mat.use_nodes=True; nodes=mat.node_tree.nodes; nodes.clear(); links=mat.node_tree.links
    shader=nodes.new('ShaderNodeBsdfPrincipled')
    shader.inputs['Base Color'].default_value=(.95,.48,.08,1)
    shader.inputs['Metallic'].default_value=.95; shader.inputs['Roughness'].default_value=.34
    noise=nodes.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value=125
    bump=nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.2; bump.inputs['Distance'].default_value=.002
    links.new(noise.outputs['Fac'],bump.inputs['Height']); links.new(bump.outputs['Normal'],shader.inputs['Normal'])
    output=nodes.new('ShaderNodeOutputMaterial'); links.new(shader.outputs['BSDF'],output.inputs['Surface'])
    return mat


def rounded(points, radius=.035, closed=False):
    points = [Vector(p) for p in points]; result = []
    for i, p in enumerate(points):
        if not closed and i in (0, len(points) - 1):
            result.append(p); continue
        before, after = points[i - 1], points[(i + 1) % len(points)]
        cut = min(radius, (p - before).length * .4, (after - p).length * .4)
        a = p + (before - p).normalized() * cut
        b = p + (after - p).normalized() * cut
        result.extend((1-t)**2*a + 2*(1-t)*t*p + t*t*b for t in map(float,np.linspace(0, 1, 7)))
    return result


class Geometry:
    def __init__(self):
        self.vertices, self.faces, self.uvs, self.smooth, self.materials = [], [], [], [], []
        self.surface = 0

    def face(self, corners, uv, smooth=False):
        start = len(self.vertices)
        self.vertices.extend(tuple(p) for p in corners)
        self.faces.append(tuple(range(start, start + len(corners))))
        self.uvs.append(uv); self.smooth.append(smooth)
        self.materials.append(self.surface)

    def tube(self, points, radius=.014, closed=False):
        self.surface = 1
        path = rounded(points, closed=closed); rings = []
        for i, point in enumerate(path):
            prev = path[(i-1) % len(path)] if closed or i else path[0]
            nxt = path[(i+1) % len(path)] if closed or i < len(path)-1 else path[-1]
            tangent = (nxt - prev).normalized()
            guide = Vector((1, 0, 0)) if abs(tangent.x) < .8 else Vector((0, 0, 1))
            a = tangent.cross(guide).normalized(); b = tangent.cross(a).normalized()
            rings.append([point + radius*(math.cos(t)*a + math.sin(t)*b)
                          for t in np.linspace(0, math.tau, 12, endpoint=False)])
        count = len(path) if closed else len(path)-1
        for i in range(count):
            nxt = (i+1) % len(path)
            for j in range(12):
                k = (j+1) % 12
                # Wrinkled octagonal foil on StrutOcto2HB / TrussFFoilA, away from seams.
                uv = [(FOIL_U[0]+(FOIL_U[1]-FOIL_U[0])*u/12,
                       1-FOIL_V[1]+(FOIL_V[1]-FOIL_V[0])*v) for u, v in [(j,i/len(path)),
                       (j+1,i/len(path)), (j+1,(i+1)/len(path)), (j,(i+1)/len(path))]]
                self.face([rings[i][j], rings[i][k], rings[nxt][k], rings[nxt][j]], uv, True)
        if not closed:
            for ring in (rings[0], rings[-1]):
                self.face(ring, [(.55+.052*math.cos(t),.56+.047*math.sin(t))
                                for t in np.linspace(0,math.tau,len(ring),endpoint=False)])

    def ribbon(self, point, count=100, normal=Vector((0,0,1))):
        # Closed, thin webbing solid; actual alternating over/under geometry.
        self.surface = 0
        thickness = normal * .0022
        for i in range(count):
            t0, t1 = i/count, (i+1)/count
            for offset in (Vector((0,0,0)), -thickness):
                corners = [point(t,w) + offset for t,w in [(t0,-.5),(t1,-.5),(t1,.5),(t0,.5)]]
                self.face(corners,[(.015,t0),(.015,t1),(.765,t1),(.765,t0)], True)
            for w in (-.5, .5):
                a, b = point(t0,w), point(t1,w)
                self.face([a,b,b-thickness,a-thickness],[(.02,t0),(.02,t1),(.024,t1),(.024,t0)])
        for t in (0,1):
            a,b=point(t,-.5),point(t,.5)
            self.face([a,b,b-thickness,a-thickness],[(.02,.5),(.76,.5),(.76,.51),(.02,.51)])

    def arm_pad(self, x):
        self.surface = 0
        ring = rounded([(x-.0375,-.265,.515),(x+.0375,-.265,.515),
                        (x+.0375,.175,.515),(x-.0375,.175,.515)],radius=.022,closed=True)
        lower = [p-Vector((0,0,.020)) for p in ring]
        uv = lambda p: (.85+.12*(p.x-x+.0375)/.075,(p.y+.265)/.44)
        for surface in (ring,lower): self.face(surface,[uv(p) for p in surface])
        for i in range(len(ring)):
            j=(i+1)%len(ring)
            self.face([ring[i],ring[j],lower[j],lower[i]],[(.86,0),(.95,0),(.95,1),(.86,1)],True)

    def object(self, mat, collection):
        mesh = bpy.data.meshes.new(NAME); mesh.from_pydata(self.vertices, [], self.faces); mesh.update()
        layer = mesh.uv_layers.new(name='UVMap')
        for face, uv, smooth, material_index in zip(mesh.polygons, self.uvs, self.smooth, self.materials):
            face.use_smooth = smooth
            face.material_index = material_index
            for i, coordinate in zip(face.loop_indices, uv): layer.data[i].uv = coordinate
        bm = bmesh.new(); bm.from_mesh(mesh)
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.000001)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bmesh.ops.triangulate(bm, faces=list(bm.faces)); bm.to_mesh(mesh); bm.free()
        obj = bpy.data.objects.new(NAME, mesh); collection.objects.link(obj)
        mesh.materials.append(mat); mesh.materials.append(gold_preview())
        return obj


def build():
    old = bpy.data.collections.get(COLLECTION)
    if old:
        for obj in list(old.objects): bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(old)
    for mesh in list(bpy.data.meshes):
        if mesh.name.startswith(NAME) and mesh.users == 0: bpy.data.meshes.remove(mesh)
    collection = bpy.data.collections.new(COLLECTION); bpy.context.scene.collection.children.link(collection)
    geo = Geometry(); z = .30
    geo.tube([(-.274,-.255,z),(.274,-.255,z),(.274,.145,z),(-.274,.145,z)], closed=True)
    geo.tube([(-.274,.145,z),(-.274,.285,.82),(.274,.285,.82),(.274,.145,z)])
    geo.tube([(-.274,-.20,z),(-.274,-.30,.014),(.274,-.30,.014),(.274,-.20,z)])
    geo.tube([(-.274,.19,.49),(-.274,.29,.014),(.274,.29,.014),(.274,.19,.49)])
    for side in (-1,1):
        geo.tube([(side*.274,-.20,z),(side*.306,-.20,.49),(side*.306,.185,.49)])
        # Broad rounded white plastic arm pad, like the reference folding chairs.
        geo.arm_pad(side*.306)
    wave = lambda v: math.tanh(3*math.cos(math.pi*v))/math.tanh(3)
    def panel(back):
        # p runs front-to-back on the seat, bottom-to-top on the backrest.
        length = .54 if back else .40
        def surface(x,p,offset):
            if back: return Vector((x,.145+p*.14/.54,.30+p*.52/.54)) + Vector((0,-offset,0))
            return Vector((x,-.255+p,.30-.004*math.sin(math.pi*p/.40)+offset))
        rows = [.09,.27,.45] if back else [.065,.20,.335]
        spacing = rows[1]-rows[0]
        normal = Vector((0,-.52,.14)).normalized() if back else Vector((0,0,1))
        for i, center in enumerate((-.18,0,.18)):
            def longitudinal(t,w,i=i,center=center):
                p=t*length
                return surface(center+w*.136,p,.0045*((-1)**i)*wave((p-rows[0])/spacing))
            geo.ribbon(longitudinal,normal=normal)
        for j, center in enumerate(rows):
            def transverse(t,w,j=j,center=center):
                x=-.274+t*.548
                return surface(x,center+w*(.118 if back else .105),-.0045*((-1)**j)*wave((x+.18)/.18))
            geo.ribbon(transverse,normal=normal)
    panel(False); panel(True)
    obj = geo.object(material(), collection)
    obj['description'] = 'Rainbow webbed beach chair, Core gold foil frame, white plastic armrests'
    print('Created',len(obj.data.vertices),'vertices;',len(obj.data.polygons),'faces')
    return obj


def export(obj):
    from io_scene_gltf2 import ExportGLTF2
    base = next(c for c in ExportGLTF2.__mro__ if 'export_format' in getattr(c,'__annotations__',{}))
    formats = base.__annotations__['export_format'].keywords['items'](None,bpy.context)
    binary = next(item[0] for item in formats if '.glb' in item[1].lower())
    frame = Matrix(((0,-1,0,.01404),(-1,0,0,.00495),(0,0,-1,GROUND),(0,0,0,1)))
    obj.name=NAME+'_authoring'; obj.data.name=obj.name; clones=[]
    for suffix, material_index in [('',0),('_Frame',1),('_VM',None)]:
        mesh=obj.data.copy(); mesh.name=NAME+suffix
        if material_index is not None:
            bm=bmesh.new(); bm.from_mesh(mesh)
            bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.material_index != material_index],context='FACES')
            bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
            bm.to_mesh(mesh); bm.free()
        chosen=obj.data.materials[material_index or 0]
        mesh.materials.clear(); mesh.materials.append(chosen)
        for polygon in mesh.polygons: polygon.material_index=0
        mesh.transform(frame)
        clone=bpy.data.objects.new(mesh.name,mesh); bpy.context.scene.collection.objects.link(clone); clones.append(clone)
    bpy.ops.object.select_all(action='DESELECT')
    for clone in clones: clone.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(ROOT/'Meshes/WingWalker_BeachChair.glb'),export_format=binary,
        use_selection=True,export_yup=False,export_animations=False,export_skins=False,export_morph=False,
        export_tangents=True,export_materials='EXPORT')
    for clone in clones:
        mesh=clone.data; bpy.data.objects.remove(clone,do_unlink=True); bpy.data.meshes.remove(mesh)
    obj.name=NAME; obj.data.name=NAME
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active=obj


if __name__ == '__main__':
    chair = build()
    export(chair)

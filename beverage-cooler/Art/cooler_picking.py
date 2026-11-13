"""Cheap open-tub/lid picking proxies, independent from render tessellation.

Small double-sided planes include protruding hardware in KSA's picking-derived
bounds. They omit screws and chain links and follow the existing Body/Lid slots.
"""
import bpy
import bmesh
from mathutils import Matrix


def proxies(frame, hinge, prefix, ring):
    result = []
    for part in ('Body', 'Lid'):
        vertices, faces = [], []

        def profile(rows):
            loops = [ring(*row, segments=1) for row in rows]
            n = len(loops[0])
            start = len(vertices)
            vertices.extend(p for loop in loops for p in loop)
            faces.extend(tuple(start + index for index in
                (i*n+j, i*n+(j+1)%n, (i+1)*n+(j+1)%n, (i+1)*n+j))
                for i in range(len(loops)-1) for j in range(n))
            faces.append(tuple(start + i for i in reversed(range(n))))
            faces.append(tuple(start + i for i in range((len(loops)-1)*n, len(loops)*n)))

        def plane(corners):
            start = len(vertices)
            vertices.extend(corners)
            face = tuple(range(start, start+4))
            faces.extend((face, tuple(reversed(face))))

        if part == 'Body':
            profile([(.65,.40,.045,0), (.65,.40,.045,.407),
                     (.576,.326,.030,.407), (.55,.30,.028,.112)])
            for side in (-1, 1):
                # Handle silhouette/bounds, not every wire/grip triangle.
                x = side * .361
                plane([(x,-.067,.235),(x,.067,.235),(x,.067,.325),(x,-.067,.325)])
            plane([(.2045,-.234,.3375),(.2435,-.234,.3375),
                   (.2435,-.234,.3465),(.2045,-.234,.3465)])
            plane([(-.221,.206,.3725),(.221,.206,.3725),
                   (.221,.206,.4075),(-.221,.206,.4075)])
        else:
            profile([(.66,.41,.046,.408), (.66,.41,.046,.463)])
            plane([(-.073,-.227,.423),(.073,-.227,.423),
                   (.073,-.227,.453),(-.073,-.227,.453)])
            plane([(-.226,.213,.402),(.226,.213,.402),
                   (.226,.213,.419),(-.226,.213,.419)])
        mesh = bpy.data.meshes.new(prefix + part + '_VM')
        mesh.from_pydata(vertices, [], faces)
        mesh.update()
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bmesh.ops.triangulate(bm, faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
        if part == 'Lid':
            mesh.transform(Matrix.Translation(-hinge))
        mesh.transform(frame)
        obj = bpy.data.objects.new(mesh.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        result.append(obj)
    return result

"""Coarse panels/leg strips for chair picking, retaining gaps between the legs."""
import bpy
import bmesh
from mathutils import Vector


def proxy(name, frame):
    vertices,faces=[],[]

    def face(points, two_sided=False):
        start=len(vertices)
        vertices.extend(points)
        item=tuple(range(start,len(vertices)))
        faces.append(item)
        if two_sided:
            # Separate vertices prevent duplicate-polygon cleanup from dropping a side.
            face(list(reversed(points)))

    def box(center, axes):
        center=Vector(center)
        axes=[Vector(v)*.5 for v in axes]
        points=[center+x*axes[0]+y*axes[1]+z*axes[2]
                for x,y,z in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
                              (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
        for indices in [(0,3,2,1),(4,5,6,7),(0,1,5,4),
                        (1,2,6,5),(2,3,7,6),(3,0,4,7)]:
            face([points[i] for i in indices])

    box((0,-.055,.300),[(.58,0,0),(0,.43,0),(0,0,.04)])
    # Slanted back slab encloses the low-poly woven back without matching every strip.
    box((0,.215,.568),[(.58,0,0),(0,.176,.554),(0,.036,-.010)])
    for side in (-1,1):
        box((side*.306,-.045,.505),[(.075,0,0),(0,.44,0),(0,0,.020)])
        x=side*.291
        for y0,z0,y1,z1 in [(-.30,0,-.20,.316),(.29,0,.19,.506)]:
            face([(x,y0-.022,z0),(x,y0+.022,z0),
                  (x,y1+.022,z1),(x,y1-.022,z1)],True)
        face([(side*.318,-.22,.295),(side*.318,-.18,.295),
              (side*.318,-.18,.51),(side*.318,-.22,.51)],True)
    for y in (-.30,.29):
        box((0,y,.020),[(.58,0,0),(0,.044,0),(0,0,.040)])
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata(vertices,[],faces)
    mesh.update()
    bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bm.to_mesh(mesh);bm.free()
    mesh.transform(frame)
    obj=bpy.data.objects.new(name,mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj

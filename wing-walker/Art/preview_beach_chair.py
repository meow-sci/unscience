"""Portable chair source and studio preview; optional native maps stay transient."""
from pathlib import Path
import os

import bpy
import numpy as np
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
NAME='Unscience_WingWalker_BeachChair'


def studio():
    old=bpy.data.scenes.get('Beach Chair Studio')
    if old:
        bpy.data.scenes.remove(old)
    scene=bpy.data.scenes.new('Beach Chair Studio')
    scene.render.engine=bpy.context.scene.render.engine
    scene.collection.children.link(bpy.data.collections['WingWalker_BeachChair'])
    scene.world=bpy.data.worlds.new('Chair studio world')
    scene.world.use_nodes=True
    background=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
    background.inputs['Color'].default_value=(.45,.47,.50,1)
    background.inputs['Strength'].default_value=.35
    for label,position,power,size in [('Key',(1,-1,2),130,1.8),
            ('Fill',(-1,-.3,1),90,1.3),('Rim',(.1,1,1.5),120,1.1)]:
        data=bpy.data.lights.new('Chair '+label,'AREA')
        data.energy,data.size=power,size
        obj=bpy.data.objects.new(data.name,data);scene.collection.objects.link(obj)
        obj.location=position
        obj.rotation_euler=(Vector((0,0,.4))-obj.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('Chair preview camera')
    camera=bpy.data.objects.new(data.name,data);scene.collection.objects.link(camera)
    camera.location=(1.25,-1.8,1.20)
    camera.rotation_euler=(Vector((0,0,.415))-camera.location).to_track_quat('-Z','Y').to_euler()
    data.type,data.ortho_scale='ORTHO',1.10
    scene.camera=camera
    scene.render.resolution_x=scene.render.resolution_y=1100
    scene.render.resolution_percentage=100
    formats=bpy.types.ImageFormatSettings.bl_rna.properties['file_format'].enum_items
    scene.render.image_settings.file_format=next(i.identifier for i in formats if i.identifier=='PNG')
    bpy.context.window.scene=scene
    return scene


def native_preview(directory):
    """Use decoded Core maps only for a render, never in the saved source/GLB."""
    mat=bpy.data.materials.new('Chair transient native foil')
    mat.use_nodes=True
    nodes,links=mat.node_tree.nodes,mat.node_tree.links
    nodes.clear()
    shader=nodes.new('ShaderNodeBsdfPrincipled')
    out=nodes.new('ShaderNodeOutputMaterial');links.new(shader.outputs['BSDF'],out.inputs['Surface'])
    images=[]
    for suffix in ('Diffuse','Normal','PBR'):
        path=Path(directory)/('CoreSurfacesB_'+suffix+'.png')
        img=bpy.data.images.load(str(path),check_existing=False)
        img.name='Chair transient '+suffix
        img.colorspace_settings.name='sRGB' if suffix=='Diffuse' else 'Non-Color'
        if suffix=='Normal':
            # BC5 supplies only XY; reconstruct Z instead of using decoded zero blue.
            values=np.empty(len(img.pixels),np.float32);img.pixels.foreach_get(values)
            pixels=values.reshape(-1,4);xy=pixels[:,:2]*2-1
            pixels[:,2]=.5+.5*np.sqrt(np.maximum(0,1-np.sum(xy*xy,axis=1)))
            img.pixels.foreach_set(values);img.update()
        images.append(img)
        tex=nodes.new('ShaderNodeTexImage');tex.image=img
        if suffix=='Diffuse':
            links.new(tex.outputs['Color'],shader.inputs['Base Color'])
        elif suffix=='Normal':
            normal=nodes.new('ShaderNodeNormalMap')
            links.new(tex.outputs['Color'],normal.inputs['Color'])
            links.new(normal.outputs['Normal'],shader.inputs['Normal'])
        else:
            channels=nodes.new('ShaderNodeSeparateColor');links.new(tex.outputs['Color'],channels.inputs['Color'])
            links.new(channels.outputs['Green'],shader.inputs['Roughness'])
            links.new(channels.outputs['Blue'],shader.inputs['Metallic'])
    return mat,images


def preview(native_maps=None):
    scene=studio();obj=bpy.data.objects[NAME]
    for img in bpy.data.images:
        if img.name.startswith('WingWalker_BeachChair_') and img.source=='FILE':
            img.pack()
    # Save a portable procedural gold proxy; native texture payloads are excluded.
    bpy.data.libraries.write(str(ROOT/'Art/BeachChair.blend'),{scene},fake_user=True,compress=True)
    original=obj.data.materials[1];temporary=None;images=[]
    try:
        if native_maps:
            temporary,images=native_preview(native_maps)
            obj.data.materials[1]=temporary
        scene.render.filepath=str(ROOT/'Art/BeachChair-preview.png')
        bpy.ops.render.render(write_still=True)
    finally:
        obj.data.materials[1]=original
        if temporary:
            bpy.data.materials.remove(temporary)
        for img in images:
            bpy.data.images.remove(img)
    print('Saved portable chair source and preview; native preview maps not retained')


if __name__=='__main__':
    preview(os.environ.get('KSA_CHAIR_PREVIEW_TEXTURES'))

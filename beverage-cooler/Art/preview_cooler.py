"""Isolated studio previews and a portable source .blend; preserve other scenes."""
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]


def studio():
    old = bpy.data.scenes.get('Beverage Cooler Studio')
    if old:
        bpy.data.scenes.remove(old)
    scene = bpy.data.scenes.new('Beverage Cooler Studio')
    scene.render.engine = bpy.context.scene.render.engine
    scene.collection.children.link(bpy.data.collections['Unscience_BeverageCooler'])
    scene.world = bpy.data.worlds.new('Cooler studio world')
    scene.world.use_nodes = True
    background = next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND')
    background.inputs['Color'].default_value = (.5,.52,.55,1)
    background.inputs['Strength'].default_value = .4
    for name, position, power, size in [('Key', (1,-1,2), 100, 2),
            ('Softbox left', (-1,-.2,.9), 70, 1.4), ('Rim', (.2,1,1.3), 100, 1.2)]:
        data = bpy.data.lights.new('Cooler ' + name,'AREA')
        data.energy, data.size = power, size
        obj = bpy.data.objects.new(data.name,data)
        scene.collection.objects.link(obj)
        obj.location = position
        obj.rotation_euler = (Vector((0,0,.25))-obj.location).to_track_quat('-Z','Y').to_euler()
    data = bpy.data.cameras.new('Cooler preview camera')
    camera = bpy.data.objects.new(data.name,data)
    scene.collection.objects.link(camera)
    camera.location = (1.1,-1.6,1.05)
    camera.rotation_euler = (Vector((0,0,.33))-camera.location).to_track_quat('-Z','Y').to_euler()
    data.type, data.ortho_scale = 'ORTHO', 1.02
    scene.camera = camera
    scene.render.resolution_x, scene.render.resolution_y = 1000, 1000
    scene.render.resolution_percentage = 100
    formats = bpy.types.ImageFormatSettings.bl_rna.properties['file_format'].enum_items
    scene.render.image_settings.file_format = next(i.identifier for i in formats if i.identifier == 'PNG')
    scene.render.film_transparent = False
    scene.frame_start, scene.frame_end = 1, 37
    scene.render.fps = 30
    bpy.context.window.scene = scene
    return scene


def previews():
    scene = studio()
    shell = [bpy.data.objects['Unscience_Cooler_'+p+'Shell'] for p in ('Body','Lid')]
    for name, material, frame in [('Red-closed','Red',1),('Red-open','Red',37),
                                  ('Stainless-closed','Steel',1)]:
        scene.frame_set(frame)
        target = Vector((0,.04,.43)) if frame == 37 else Vector((0,0,.25))
        scene.camera.rotation_euler = (target-scene.camera.location).to_track_quat('-Z','Y').to_euler()
        scene.camera.data.ortho_scale = 1.08 if frame == 37 else .94
        for obj in shell:
            obj.data.materials[0] = bpy.data.materials['Cooler_'+material]
        scene.render.filepath = str(ROOT / 'Art' / (name+'.png'))
        bpy.ops.render.render(write_still=True)
    scene.frame_set(1)
    for obj in shell:
        obj.data.materials[0] = bpy.data.materials['Cooler_Red']
    for img in bpy.data.images:
        if img.name.startswith('BeverageCooler_') and img.source == 'FILE':
            img.pack()
    bpy.data.libraries.write(str(ROOT / 'Art/BeverageCooler.blend'), {scene},
                             fake_user=True, compress=True)
    print('Saved portable source and three cooler previews')


if __name__ == '__main__':
    previews()

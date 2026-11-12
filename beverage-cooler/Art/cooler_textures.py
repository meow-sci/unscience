"""Original, deterministic 512px PBR maps for the beverage cooler (Blender)."""
import math
from pathlib import Path

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[1]


def image(name, pixels, data=False):
    old = bpy.data.images.get(name)
    if old:
        bpy.data.images.remove(old)
    height, width, _ = pixels.shape
    img = bpy.data.images.new(name, width=width, height=height, alpha=True)
    img.colorspace_settings.name = 'Non-Color'
    img.pixels.foreach_set(pixels.astype(np.float32).ravel())
    img.filepath_raw = str(ROOT / 'Textures' / (name + '.png'))
    formats = bpy.types.ImageFormatSettings.bl_rna.properties['file_format'].enum_items
    img.file_format = next(item.identifier for item in formats if item.identifier == 'PNG')
    img.save()
    path = img.filepath_raw
    bpy.data.images.remove(img)
    img = bpy.data.images.load(path, check_existing=False)
    img.name = name
    img.colorspace_settings.name = 'Non-Color' if data else 'sRGB'
    return img


def materials():
    size = 512
    y, x = np.mgrid[0:size, 0:size] / size
    rng = np.random.default_rng(20261111)
    grain = rng.normal(0, .004, (size, size))
    brush = rng.normal(0, .012, size)[None, :] + .004 * np.sin(x * math.tau * 181)
    result = {}
    for name in ('Red', 'Steel', 'Common'):
        color = np.ones((size, size, 4))
        normal = np.ones_like(color)
        normal[:, :, :3] = [.5, .5, 1]
        orm = np.ones_like(color)
        orm[:, :, 2] = 0
        if name == 'Red':
            color[:, :, :3] = np.array([.70, .105, .075]) * (1 + grain[:, :, None])
            orm[:, :, 1] = .285 + grain
            normal[:, :, 0] += grain * .25
            normal[:, :, 1] += np.roll(grain, 1, axis=0) * .25
        elif name == 'Steel':
            color[:, :, :3] = np.array([.70, .72, .73]) * (1 + .3 * brush[:, :, None])
            orm[:, :, 1] = .32 + .4 * brush
            orm[:, :, 2] = 1
        else:
            # Four atlas strips: white liner, black rubber, chrome, dark hardware.
            for band, rgb, rough, metal in [(0, [.91, .92, .89], .43, 0),
                    (1, [.045, .05, .052], .76, 0),
                    (2, [.68, .71, .72], .22, 1),
                    (3, [.10, .11, .115], .40, .5)]:
                mask = (x >= band / 4) & (x < (band + 1) / 4)
                color[mask, :3] = np.array(rgb) * (1 + grain[mask, None])
                orm[mask, 1] = rough + grain[mask]
                orm[mask, 2] = metal
                normal[mask, 0] += grain[mask] * .35
        maps = [image('BeverageCooler_' + name + '_' + suffix, pixels, linear)
                for suffix, pixels, linear in [('Diffuse', color, False),
                    ('Normal', normal, True), ('ORM', orm, True)]]
        mat = bpy.data.materials.get('Cooler_' + name) or bpy.data.materials.new('Cooler_' + name)
        mat.use_nodes = True
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        nodes.clear()
        shader = nodes.new('ShaderNodeBsdfPrincipled')
        out = nodes.new('ShaderNodeOutputMaterial')
        links.new(shader.outputs['BSDF'], out.inputs['Surface'])
        for i, img in enumerate(maps):
            tex = nodes.new('ShaderNodeTexImage')
            tex.image = img
            if i == 0:
                links.new(tex.outputs['Color'], shader.inputs['Base Color'])
            elif i == 1:
                node = nodes.new('ShaderNodeNormalMap')
                links.new(tex.outputs['Color'], node.inputs['Color'])
                links.new(node.outputs['Normal'], shader.inputs['Normal'])
            else:
                node = nodes.new('ShaderNodeSeparateColor')
                links.new(tex.outputs['Color'], node.inputs['Color'])
                links.new(node.outputs['Green'], shader.inputs['Roughness'])
                links.new(node.outputs['Blue'], shader.inputs['Metallic'])
        result[name] = mat
    return result

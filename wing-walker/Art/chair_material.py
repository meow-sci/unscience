"""Original 256px sun-faded, abraded rainbow webbing and used plastic atlas."""
from pathlib import Path
import math
import runpy

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
NAME = 'Unscience_WingWalker_BeachChair'
image = runpy.run_path(str(ROOT / 'Art/create_standing_mount.py'))['image']


def material():
    size = 256
    y, x = np.mgrid[0:size, 0:size] / size
    rng = np.random.default_rng(9081)
    grain = rng.normal(0, .012, (size, size))
    weave_x = np.sin(x * math.tau * 64)
    weave_y = np.sin(y * math.tau * 80)
    weave = .022 * weave_x * weave_y + grain
    colors = np.array([[.78,.15,.15],[.88,.39,.15],[.87,.73,.24],
                       [.20,.57,.31],[.19,.40,.66]])
    stripe = np.clip(((x-.015)/.75*5).astype(int), 0, 4)
    color = np.ones((size,size,4))
    color[:,:,:3] = colors[stripe]
    wear = .30 + .16 * np.sin(y*math.tau*1.3 + x*8)**2
    dirt = .10 + .07*np.sin(y*9+x*11)**2
    for cx,cy,sx,sy in [(.18,.46,.09,.12),(.58,.64,.13,.16),(.37,.16,.06,.06),
                       (.08,.82,.07,.09),(.70,.31,.04,.11)]:
        wear += .30*np.exp(-((x-cx)/sx)**2-((y-cy)/sy)**2)
        dirt += .42*np.exp(-((x-cx+.025)/(sx*.85))**2-((y-cy-.03)/(sy*.9))**2)
    wear=np.clip(wear,0,.85)
    dirt=np.clip(dirt,0,.75)
    color[:,:,:3] = color[:,:,:3]*(1-wear[:,:,None]) + np.array([.51,.46,.34])*wear[:,:,None]
    color[:,:,:3] *= (1+weave-dirt)[:,:,None]
    # Broad ingrained brown/grey soil must read at gameplay distance, not only as fine noise.
    grime=.18+.26*np.exp(-((y-.51)/.31)**2)*(np.sin(x*29+y*8)**2)
    color[:,:,:3] = color[:,:,:3]*(1-grime[:,:,None])+np.array([.24,.21,.15])*grime[:,:,None]
    # Broken pale fibers and rubbed patches; no extra mesh fibers or modeled tears.
    for _ in range(150):
        cx,cy = rng.uniform(.02,.77),rng.uniform(0,1)
        scratch = (abs(x-cx)<rng.uniform(.001,.003)) & (abs(y-cy)<rng.uniform(.004,.022))
        scratch &= rng.random(x.shape)>.32
        color[scratch,:3] = color[scratch,:3]*.45 + np.array([.59,.54,.40])*.55
    selvedge = (x < .029) | ((x > .751) & (x < .80))
    edge_fuzz = .07*np.sin(y*157)**2 + grain
    color[selvedge,:3] = (np.array([.47,.42,.30])[None,:]
                          * (1+edge_fuzz[selvedge]-dirt[selvedge])[:,None])
    plastic = x>.80
    plastic_soil = .055*np.sin(y*13+x*23)**2 + .02*np.cos(y*37)**2
    color[plastic,:3] = np.array([.83,.84,.78])*(1+grain[plastic]-plastic_soil[plastic])[:,None]
    normal = np.ones_like(color)
    normal[:,:,0] = .5 + .028*weave_x*(1-wear*.7)
    normal[:,:,1] = .5 + .028*weave_y*(1-wear*.7)
    normal[:,:,2] = 1
    normal[plastic,0] = .5+grain[plastic]*.1
    normal[plastic,1] = .5+grain[plastic]*.1
    orm = np.ones_like(color)
    orm[:,:,1] = np.clip(.88+wear*.12+grain,0,1)
    orm[:,:,2] = 0
    orm[plastic,1] = .62+plastic_soil[plastic]
    maps = [image('WingWalker_BeachChair_'+suffix,np.clip(data,0,1),linear)
            for suffix,data,linear in [('Diffuse',color,False),('Normal',normal,True),('ORM',orm,True)]]
    mat = bpy.data.materials.get(NAME) or bpy.data.materials.new(NAME)
    mat.use_nodes=True
    nodes,links=mat.node_tree.nodes,mat.node_tree.links
    nodes.clear()
    shader=nodes.new('ShaderNodeBsdfPrincipled')
    output=nodes.new('ShaderNodeOutputMaterial')
    links.new(shader.outputs['BSDF'],output.inputs['Surface'])
    for index,img in enumerate(maps):
        tex=nodes.new('ShaderNodeTexImage');tex.image=img
        if index==0:
            links.new(tex.outputs['Color'],shader.inputs['Base Color'])
        elif index==1:
            node=nodes.new('ShaderNodeNormalMap')
            links.new(tex.outputs['Color'],node.inputs['Color'])
            links.new(node.outputs['Normal'],shader.inputs['Normal'])
        else:
            node=nodes.new('ShaderNodeSeparateColor')
            links.new(tex.outputs['Color'],node.inputs['Color'])
            links.new(node.outputs['Green'],shader.inputs['Roughness'])
            links.new(node.outputs['Blue'],shader.inputs['Metallic'])
    return mat

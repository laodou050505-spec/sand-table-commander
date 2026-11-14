"""多材质版烘焙：每个原材质用自己的贴图（SrcUV）烘到共享的新 UV（UV0）上；另烘位置、法线、来源材质 ID。"""
import bpy, numpy as np
out = '/tmp/helm'
bpy.ops.wm.open_mainfile(filepath=f'{out}/weld.blend')
ob = bpy.data.objects['Helm']; me = ob.data
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
uv = np.load(f'{out}/loop_uv.npy')
L = me.uv_layers.new(name='UV0'); L.data.foreach_set('uv', uv.ravel())
me.uv_layers.active = L; L.active_render = True
srcimg = {}
for m in me.materials:
    ims = [n.image for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image]
    srcimg[m.name] = ims[0] if ims else None
print('SRC', {k: (v.name if v else None) for k, v in srcimg.items()})
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 1; sc.cycles.device = 'CPU'
ids = {'Knight_src': 0.2, 'Orc_src': 0.5, 'Lion_src': 0.8}


def bake(name, kind):
    img = bpy.data.images.new(name, 2048, 2048, float_buffer=True); img.colorspace_settings.name = 'Non-Color'
    for m in me.materials:
        nt = m.node_tree; nt.nodes.clear()
        o = nt.nodes.new('ShaderNodeOutputMaterial'); em = nt.nodes.new('ShaderNodeEmission'); nt.links.new(em.outputs[0], o.inputs[0])
        if kind == 'alb':
            u = nt.nodes.new('ShaderNodeUVMap'); u.uv_map = 'SrcUV'
            t = nt.nodes.new('ShaderNodeTexImage'); t.image = srcimg[m.name]; t.interpolation = 'Closest'
            nt.links.new(u.outputs[0], t.inputs[0]); nt.links.new(t.outputs[0], em.inputs[0])
        elif kind == 'pos':
            tc = nt.nodes.new('ShaderNodeTexCoord'); nt.links.new(tc.outputs['Object'], em.inputs[0])
        elif kind == 'nrm':
            g = nt.nodes.new('ShaderNodeNewGeometry'); nt.links.new(g.outputs['Normal'], em.inputs[0])
        elif kind == 'id':
            v = ids.get(m.name, 0.0); em.inputs[0].default_value = (v, v, v, 1)
        it = nt.nodes.new('ShaderNodeTexImage'); it.image = img; nt.nodes.active = it
    sc.render.bake.margin = 0 if kind == 'id' else 8
    bpy.ops.object.bake(type='EMIT')
    img.filepath_raw = f'{out}/{name}.exr'; img.file_format = 'OPEN_EXR'; img.save(); print('saved', name)


bake('src_albedo', 'alb'); bake('pos', 'pos'); bake('nrm', 'nrm'); bake('srcid', 'id')
# 原材质换成一个空材质，后面的烘焙脚本只认 UV0
mat = bpy.data.materials.new('Helm')
me.materials.clear(); me.materials.append(mat)
bpy.ops.wm.save_as_mainfile(filepath=f'{out}/stage1.blend')
print('DONE')

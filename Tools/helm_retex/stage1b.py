import bpy, sys, numpy as np
out = '/tmp/helm'
bpy.ops.wm.open_mainfile(filepath=f'{out}/weld.blend')
ob = bpy.data.objects['Helm']; me = ob.data
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
uv = np.load(f'{out}/loop_uv.npy')
L = me.uv_layers.new(name='UV0')
L.data.foreach_set('uv', uv.ravel())
me.uv_layers.active = L; L.active_render = True
albedo = [i for i in bpy.data.images if i.size[0] > 0][0]
sc = bpy.context.scene; sc.render.engine='CYCLES'; sc.cycles.samples=1; sc.cycles.device='CPU'; sc.render.bake.margin = 8
def bake(name, build):
    mat = bpy.data.materials.new(name); mat.use_nodes=True; nt=mat.node_tree; nt.nodes.clear()
    em = nt.nodes.new('ShaderNodeEmission'); o = nt.nodes.new('ShaderNodeOutputMaterial'); nt.links.new(em.outputs[0], o.inputs[0])
    build(nt, em)
    img = bpy.data.images.new(name, 2048, 2048, float_buffer=True); img.colorspace_settings.name = 'Non-Color'
    it = nt.nodes.new('ShaderNodeTexImage'); it.image = img; nt.nodes.active = it
    me.materials.clear(); me.materials.append(mat)
    bpy.ops.object.bake(type='EMIT')
    img.filepath_raw = f'{out}/{name}.exr'; img.file_format='OPEN_EXR'; img.save(); print('saved', name)
def b_alb(nt, em):
    uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = 'SrcUV'
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = albedo; t.interpolation='Closest'
    nt.links.new(uvn.outputs[0], t.inputs[0]); nt.links.new(t.outputs[0], em.inputs[0])
def b_pos(nt, em):
    tc = nt.nodes.new('ShaderNodeTexCoord'); nt.links.new(tc.outputs['Object'], em.inputs[0])
def b_nrm(nt, em):
    g = nt.nodes.new('ShaderNodeNewGeometry'); nt.links.new(g.outputs['Normal'], em.inputs[0])
bake('src_albedo', b_alb); bake('pos', b_pos); bake('nrm', b_nrm)
me.materials.clear()
bpy.ops.wm.save_as_mainfile(filepath=f'{out}/stage1.blend')
print('DONE')

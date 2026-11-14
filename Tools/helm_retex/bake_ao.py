import bpy
out='/tmp/helm'
bpy.ops.wm.open_mainfile(filepath=f'{out}/stage1.blend')
ob=bpy.data.objects['Helm']; me=ob.data; bpy.context.view_layer.objects.active=ob; ob.select_set(True)
for p in me.polygons: p.use_smooth=True
me.uv_layers.active=me.uv_layers['UV0']
sc=bpy.context.scene; sc.render.engine='CYCLES'; sc.cycles.device='CPU'; sc.render.bake.margin=8
w=bpy.data.worlds.new('w'); sc.world=w
def mk(name, emit_build):
    mat=bpy.data.materials.new(name); mat.use_nodes=True; nt=mat.node_tree; nt.nodes.clear()
    o=nt.nodes.new('ShaderNodeOutputMaterial')
    if emit_build:
        em=nt.nodes.new('ShaderNodeEmission'); nt.links.new(em.outputs[0],o.inputs[0]); emit_build(nt,em)
    else:
        b=nt.nodes.new('ShaderNodeBsdfDiffuse'); nt.links.new(b.outputs[0],o.inputs[0])
    img=bpy.data.images.new(name,2048,2048,float_buffer=True); img.colorspace_settings.name='Non-Color'
    it=nt.nodes.new('ShaderNodeTexImage'); it.image=img; nt.nodes.active=it
    me.materials.clear(); me.materials.append(mat); return img
img=mk('ao',None); sc.cycles.samples=96; sc.world.light_settings.distance=0.04
bpy.ops.object.bake(type='AO'); img.filepath_raw=f'{out}/ao.exr'; img.file_format='OPEN_EXR'; img.save(); print('saved ao')
def pt(nt,em):
    g=nt.nodes.new('ShaderNodeNewGeometry'); nt.links.new(g.outputs['Pointiness'],em.inputs[0])
img=mk('point',pt); sc.cycles.samples=1
bpy.ops.object.bake(type='EMIT'); img.filepath_raw=f'{out}/point.exr'; img.file_format='OPEN_EXR'; img.save(); print('saved point')

import bpy
bpy.ops.wm.open_mainfile(filepath='/tmp/helm/stage1.blend')
ob=bpy.data.objects['Helm']; me=ob.data; bpy.context.view_layer.objects.active=ob; ob.select_set(True)
me.uv_layers.active=me.uv_layers['UV0']
sc=bpy.context.scene; sc.render.engine='CYCLES'; sc.cycles.samples=1; sc.render.bake.margin=0
mat=bpy.data.materials.new('m'); mat.use_nodes=True; nt=mat.node_tree; nt.nodes.clear()
o=nt.nodes.new('ShaderNodeOutputMaterial'); em=nt.nodes.new('ShaderNodeEmission'); nt.links.new(em.outputs[0],o.inputs[0]); em.inputs[0].default_value=(1,1,1,1)
img=bpy.data.images.new('mask',2048,2048,float_buffer=True); img.colorspace_settings.name='Non-Color'
it=nt.nodes.new('ShaderNodeTexImage'); it.image=img; nt.nodes.active=it
me.materials.clear(); me.materials.append(mat)
bpy.ops.object.bake(type='EMIT'); img.filepath_raw='/tmp/helm/mask.exr'; img.file_format='OPEN_EXR'; img.save(); print('saved mask')

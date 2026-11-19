import bpy, json
src='/tmp/h3r/src_parts.fbx'; dst='/Users/ld/Documents/ChatGPT/WarTable/Assets/_WarTable/Art/Models/Cover/Helm3/helm3.fbx'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
ob=[o for o in bpy.context.scene.objects if o.type=='MESH'][0]
for o in list(bpy.context.scene.objects):
    if o!=ob: bpy.data.objects.remove(o)
bpy.context.view_layer.objects.active=ob; ob.select_set(True)
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM'); bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.separate(type='MATERIAL'); bpy.ops.object.mode_set(mode='OBJECT')
# 模型约 1 m 高（场景里缩到 0.3 m）：体素 3.5 mm ≈ 场景 1 mm，面甲缝 / 骷髅眼窝都留得住
VOX={'part0':0.0035,'part1':0.0035,'part2':0.0025,'part3':0.004,'part4':0.0028,'part5':0.0025,'part6':0.0025}
TARGET={'part0':16000,'part1':12000,'part2':16000,'part3':6000,'part4':12000,'part5':6000,'part6':6000}
out={}
for o in [x for x in bpy.context.scene.objects if x.type=='MESH']:
    name=o.data.materials[o.data.polygons[0].material_index].name.split('.')[0]
    o.name=name; o.data.name=name
    bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active=o; o.select_set(True)
    n0=len(o.data.polygons)
    o.data.remesh_voxel_size=VOX[name]; o.data.remesh_voxel_adaptivity=0.0; o.data.use_remesh_preserve_volume=True
    bpy.ops.object.voxel_remesh()
    sm=o.modifiers.new('sm','SMOOTH'); sm.factor=0.5; sm.iterations=2
    bpy.ops.object.modifier_apply(modifier='sm')
    n1=len(o.data.polygons)
    if n1>TARGET[name]:
        dc=o.modifiers.new('dc','DECIMATE'); dc.ratio=TARGET[name]/n1
        bpy.ops.object.modifier_apply(modifier='dc')
    for p in o.data.polygons: p.use_smooth=True
    for l in list(o.data.uv_layers): o.data.uv_layers.remove(l)
    out[name]=(n0,n1,len(o.data.polygons))
print('parts',json.dumps(out,sort_keys=True))
bpy.ops.export_scene.fbx(filepath=dst,use_selection=False,path_mode='COPY',embed_textures=False,mesh_smooth_type='FACE')
print('exported')

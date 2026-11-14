import bpy
bpy.ops.wm.open_mainfile(filepath='/tmp/helm/stage1.blend')
ob=bpy.data.objects['Helm']; me=ob.data
me.uv_layers.remove(me.uv_layers['SrcUV']); me.uv_layers['UV0'].name='UVMap'
me.materials.clear(); mat=bpy.data.materials.new('Helm'); me.materials.append(mat)
for p in me.polygons: p.use_smooth=True
for o in list(bpy.data.objects):
    if o!=ob: bpy.data.objects.remove(o)
bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active=ob
bpy.ops.export_scene.fbx(filepath='/tmp/helm/out/helm.fbx', use_selection=True, path_mode='STRIP', embed_textures=False,
    mesh_smooth_type='OFF', apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True, add_leaf_bones=False)
print('EXPORTED', len(me.polygons))

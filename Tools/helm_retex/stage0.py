# 导入 clean.glb → 焊接 → 导出顶点/三角形给 xatlas
import bpy, sys, numpy as np, bmesh
args = sys.argv[sys.argv.index('--')+1:]
src, out = args[0], args[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type=='MESH'][0]
for o in list(bpy.context.scene.objects):
    if o != ob: bpy.data.objects.remove(o)
ob.name = 'Helm'; bpy.context.view_layer.objects.active = ob; ob.select_set(True)
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=1e-5)
bpy.ops.mesh.quads_convert_to_tris()
bpy.ops.object.mode_set(mode='OBJECT')
me = ob.data; me.uv_layers[0].name = 'SrcUV'
v = np.array([x.co[:] for x in me.vertices], np.float32)
f = np.array([p.vertices[:] for p in me.polygons], np.uint32)
np.savez(f'{out}/mesh.npz', v=v, f=f)
print('DUMP', v.shape, f.shape)
bpy.ops.wm.save_as_mainfile(filepath=f'{out}/weld.blend')

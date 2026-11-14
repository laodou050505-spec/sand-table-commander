import bpy, bmesh, numpy as np
bpy.ops.wm.open_mainfile(filepath='/tmp/helm/stage1_orig.blend')
ob=bpy.data.objects['Helm']; me=ob.data
mid=np.load('/tmp/helm/matid_final.npy'); N=mid.shape[0]
uv=np.zeros(len(me.loops)*2,np.float32); me.uv_layers['UV0'].data.foreach_get('uv',uv); uv=uv.reshape(-1,2)
vi=np.zeros(len(me.loops),np.int64); me.loops.foreach_get('vertex_index',vi)
px=np.clip((uv[:,0]*N).astype(int),0,N-1); py=np.clip(((1-uv[:,1])*N).astype(int),0,N-1)
cls=mid[py,px]
V=len(me.vertices); votes=np.zeros((V,7),np.int32)
np.add.at(votes,(vi,cls+1),1); vc=votes[:,1:].argmax(1); vc[votes[:,1:].sum(1)==0]=-1
co=np.array([v.co[:] for v in me.vertices])
bm=bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
lion=[v for v in bm.verts if co[v.index,2]>0.31]
steel=[v for v in bm.verts if vc[v.index]==0 and all(vc[e.other_vert(v).index]==0 for e in v.link_edges) and not v.is_boundary]
# 钢板内部再收一圈（离边界至少两环），避免把包边拉塌
sset=set(v.index for v in steel)
#steel=[v for v in steel if all(e.other_vert(v).index in sset for e in v.link_edges)]
print('lion', len(lion), 'steel', len(steel))
for _ in range(0): bmesh.ops.smooth_vert(bm, verts=lion, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
for _ in range(6): bmesh.ops.smooth_vert(bm, verts=steel, factor=0.4, use_axis_x=True, use_axis_y=True, use_axis_z=True)
bm.to_mesh(me); bm.free()
bpy.ops.wm.save_as_mainfile(filepath='/tmp/helm/stage1.blend'); print('SMOOTHED')

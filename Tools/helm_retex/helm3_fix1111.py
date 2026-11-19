# 11-11 封面头盔贴图修正（一）：面甲下方的开孔里透出一块黄橙色方块 = 埋在银钢面甲后面 4–5 cm 的黄铜铆钉（part5 连通块，
# 截图投影 + 射线核对：沿自身法线 4–5.5 cm 才出盔壳，从外面只能透过开孔看到）→ 删掉，开孔里露出内衬（暗色）。
# 判据：part5 里尺寸 < 5 cm、沿平均法线射出去 3 cm 以上才打到 part0/1/2 盔壳、且在正面（x > 0.2，Blender 坐标里正面朝 +X）。
# 同时导出每个部件的 UV 三角形到 /tmp/sp2/helm_uv.npz，给 helm3_detail_fix.py 用。
# 用法：Blender -b -P Tools/helm_retex/helm3_fix1111.py   （总是从 ArtSource/Cover/helm3_backup_1111/helm3.fbx 读，可重复跑）
import bpy, bmesh, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
W='/Users/ld/Documents/ChatGPT/WarTable/'
src=W+'ArtSource/Cover/helm3_backup_1111/helm3.fbx'; dst=W+'Assets/_WarTable/Art/Models/Cover/Helm3/helm3.fbx'
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=src)
objs={o.name.split('.')[0]:o for o in bpy.context.scene.objects if o.type=='MESH'}
dg=bpy.context.evaluated_depsgraph_get()
shells=[BVHTree.FromObject(objs[p],dg) for p in ('part0','part1','part2')]
def depth(p,d):
    best=None
    for t in shells:
        loc,nn,i,dist=t.ray_cast(p+d*0.004,d,0.3)
        if loc is not None and (best is None or dist<best): best=dist
    return best
o=objs['part5']; bm=bmesh.new(); bm.from_mesh(o.data); bm.faces.ensure_lookup_table()
comp=[-1]*len(bm.faces); nc=0
for f in bm.faces:
    if comp[f.index]>=0: continue
    st=[f]; comp[f.index]=nc
    while st:
        x=st.pop()
        for e in x.edges:
            for y in e.link_faces:
                if comp[y.index]<0: comp[y.index]=nc; st.append(y)
    nc+=1
kill=[]
for c in range(nc):
    fs=[f for f in bm.faces if comp[f.index]==c]
    P=np.array([tuple(v.co) for f in fs for v in f.verts]); d=P.max(0)-P.min(0)
    if d.max()>0.05: continue
    cen=Vector(tuple(P.mean(0))); n=Vector((0,0,0))
    for f in fs: n+=f.normal*f.calc_area()
    a=depth(cen,n.normalized())
    if a and a>0.03 and cen.x>0.2:
        print('KILL part5 comp',c,len(fs),tuple(round(x,3) for x in cen),round(a,4)); kill+=fs
bmesh.ops.delete(bm,geom=kill,context='FACES')
loose=[v for v in bm.verts if not v.link_faces]; bmesh.ops.delete(bm,geom=loose,context='VERTS')
bm.to_mesh(o.data); bm.free(); o.data.update()
out={}
for ob in bpy.context.scene.objects:
    if ob.type!='MESH' or not ob.data.uv_layers: continue
    me=ob.data; me.calc_loop_triangles(); uv=me.uv_layers[0].data
    out[ob.name.split('.')[0]]=np.array([[tuple(uv[l].uv) for l in t.loops] for t in me.loop_triangles],np.float32)
np.savez('/tmp/sp2/helm_uv.npz',**out)
bpy.ops.export_scene.fbx(filepath=dst,use_selection=False,path_mode='COPY',embed_textures=False)
print('exported', {k:len(v) for k,v in out.items()})

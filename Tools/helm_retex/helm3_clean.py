import bpy, bmesh, math, json
from mathutils import Vector
from mathutils.bvhtree import BVHTree
src='/Users/ld/Documents/ChatGPT/WarTable/Assets/_WarTable/Art/Models/Cover/Helm3/helm3.fbx'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
ob=[o for o in bpy.context.scene.objects if o.type=='MESH'][0]
for o in list(bpy.context.scene.objects):
    if o!=ob: bpy.data.objects.remove(o)
bpy.context.view_layer.objects.active=ob; ob.select_set(True)
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM'); bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
me=ob.data
bm=bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table(); n0=len(bm.faces)
# 连通块（按共享边）
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
# 每个连通块单独建 BVH；判断“面中心是否在另一个块的实体内部”：向 6 个方向射线，每个方向都先打到那个块的背面（射线与面法线同向）= 在内部
groups={}
for f in bm.faces: groups.setdefault(comp[f.index],[]).append(f)
big=[c for c,fs in groups.items() if len(fs)>=60]
trees={}
for c in big:
    vs=[]; idx={}; polys=[]
    for f in groups[c]:
        p=[]
        for v in f.verts:
            if v.index not in idx: idx[v.index]=len(vs); vs.append(v.co.copy())
            p.append(idx[v.index])
        polys.append(p)
    trees[c]=BVHTree.FromPolygons(vs,polys)
D=[Vector(d) for d in ((1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1))]
def inside(pt,tree):
    for d in D:
        loc,nrm,i,dist=tree.ray_cast(pt+d*1e-5,d,2.0)
        if loc is None or nrm.dot(d)<=0: return False
    return True
kill=[]
for f in bm.faces:
    c=comp[f.index]; pt=f.calc_center_median()
    for o in big:
        if o==c: continue
        if inside(pt,trees[o]): kill.append(f); break
bmesh.ops.delete(bm,geom=kill,context='FACES')
# 拓扑清理
bmesh.ops.remove_doubles(bm,verts=bm.verts,dist=2e-5)
bmesh.ops.dissolve_degenerate(bm,edges=bm.edges,dist=1e-6)
loose=[v for v in bm.verts if not v.link_faces]; bmesh.ops.delete(bm,geom=loose,context='VERTS')
bm.faces.ensure_lookup_table(); seen=set(); small=[]
for f in bm.faces:
    if f.index in seen: continue
    cc=[f]; seen.add(f.index); st=[f]
    while st:
        x=st.pop()
        for e in x.edges:
            for y in e.link_faces:
                if y.index not in seen: seen.add(y.index); cc.append(y); st.append(y)
    if len(cc)<12: small+=cc
bmesh.ops.delete(bm,geom=small,context='FACES')
bm.to_mesh(me); bm.free(); me.update()
print('faces',n0,'->',len(me.polygons),'interpenetrating',len(kill),'debris',len(small),'components',nc)
for p in me.polygons: p.use_smooth=True
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.separate(type='MATERIAL'); bpy.ops.object.mode_set(mode='OBJECT')
cnt={}
for o in bpy.context.scene.objects:
    if o.type!='MESH' or not o.data.polygons: continue
    k=o.data.materials[o.data.polygons[0].material_index].name.split('.')[0]
    o.name=k; o.data.name=k; cnt[k]=len(o.data.polygons)
print('parts',json.dumps(cnt,sort_keys=True))
bpy.ops.export_scene.fbx(filepath=src,use_selection=False,path_mode='COPY',embed_textures=False)
print('exported')

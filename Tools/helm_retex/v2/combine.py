import bpy, bmesh, math, mathutils, subprocess
bpy.ops.wm.open_mainfile(filepath='/tmp/helm2/src.blend')
K=bpy.data.objects['Knight']; O=bpy.data.objects['Orc']
S=1.30
O.scale=(S,S,S); bpy.context.view_layer.objects.active=O; O.select_set(True); bpy.ops.object.transform_apply(scale=True); O.select_set(False)
K.hide_render=False; O.hide_render=False
CUT=0.03
def keep(ob, side):
    bm=bmesh.new(); bm.from_mesh(ob.data)
    geom=bm.verts[:]+bm.edges[:]+bm.faces[:]
    n=mathutils.Vector((0,1,0))
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0,CUT,0), plane_no=n, clear_outer=(side<0), clear_inner=(side>0))
    bm.to_mesh(ob.data); bm.free()
keep(K,-1); keep(O,1)
for o in (K,O):
    for p in o.data.polygons: p.use_smooth=True
bb=lambda o:[o.matrix_world@mathutils.Vector(c) for c in o.bound_box]
for o in (K,O):
    b=bb(o); print('BB',o.name,[round(min(v[i] for v in b),3) for i in range(3)],[round(max(v[i] for v in b),3) for i in range(3)], len(o.data.polygons))
bpy.ops.wm.save_as_mainfile(filepath='/tmp/helm2/combined.blend')
# 渲染：带 P2 贴图
sc=bpy.context.scene; sc.render.engine='BLENDER_EEVEE'; sc.render.resolution_x=sc.render.resolution_y=600
w=bpy.data.worlds.new('w'); sc.world=w; w.use_nodes=True; [n for n in w.node_tree.nodes if n.type=='BACKGROUND'][0].inputs[1].default_value=3.0
sun=bpy.data.objects.new('s',bpy.data.lights.new('s','SUN')); sun.data.energy=2.5; sc.collection.objects.link(sun); sun.rotation_euler=(0.7,0.2,0.6)
for o in (K,O):
    for m in o.data.materials:
        if m and m.use_nodes:
            bs=[n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED']
            if bs: bs[0].inputs['Metallic'].default_value=0.0; bs[0].inputs['Roughness'].default_value=0.6
cam=bpy.data.objects.new('c',bpy.data.cameras.new('c')); sc.collection.objects.link(cam); sc.camera=cam
fs=[]
for d in [(1,0,0.1),(1,0.6,0.15),(1,-0.6,0.15),(0,1,0.1),(0,-1,0.1),(-1,0.3,0.4)]:
    cam.location=mathutils.Vector(d).normalized()*3.2; cam.rotation_euler=(-cam.location).to_track_quat('-Z','Y').to_euler()
    f=f'/tmp/helm2/cb_{len(fs)}.png'; sc.render.filepath=f; bpy.ops.render.render(write_still=True); fs.append(f)
subprocess.run(['python3','-c',f"from PIL import Image\nims=[Image.open(x).convert('RGB') for x in {fs!r}]\no=Image.new('RGB',(1800,1200))\n[o.paste(im,((i%3)*600,(i//3)*600)) for i,im in enumerate(ims)]\no.save('/tmp/helm2/combined.png')"])
print('DONE')

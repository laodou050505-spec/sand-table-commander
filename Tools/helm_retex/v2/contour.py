import bpy, math, mathutils, json
from mathutils.bvhtree import BVHTree
bpy.ops.wm.open_mainfile(filepath='/tmp/helm2/src.blend')
out={}
for n in ('Knight','Orc'):
    ob=bpy.data.objects[n]; me=ob.data
    bvh=BVHTree.FromObject(ob, bpy.context.evaluated_depsgraph_get())
    pts=[]
    # 外轮廓：从中线平面外侧往中心打射线
    c=mathutils.Vector((0,0,0))
    for i in range(181):
        th=math.radians(-80+i*(340/180))   # 从前下方(-80°)绕过顶部到后下方(260°)
        d=mathutils.Vector((math.cos(th),0,math.sin(th)))
        hit=bvh.ray_cast(c+d*3, -d)
        if hit[0] is not None and hit[1].dot(d)>0: pts.append((round(th,3),round(hit[0].x,4),round(hit[0].z,4)))
    # 宽度：各高度的左右外表面（y 方向，前后取中）
    wid=[]
    for k in range(21):
        z=-0.5+k*0.05
        for xs in (0.0,):
            a=bvh.ray_cast(mathutils.Vector((xs,-3,z)),mathutils.Vector((0,1,0))); b=bvh.ray_cast(mathutils.Vector((xs,3,z)),mathutils.Vector((0,-1,0)))
            if a[0] and b[0]: wid.append((round(z,2),round(a[0].y,3),round(b[0].y,3)))
    out[n]={'contour':pts,'width':wid}
    zs=[p[2] for p in pts]; xs=[p[1] for p in pts]
    print(n,'contour pts',len(pts),'x',round(min(xs),3),round(max(xs),3),'z',round(min(zs),3),round(max(zs),3))
    print(n,'width',wid)
json.dump(out,open('/tmp/helm2/contour.json','w'))

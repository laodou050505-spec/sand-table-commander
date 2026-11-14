# 11-11 封面头盔贴图修正（二）：银钢半边（part0）上一块块深灰色的尖角斑（P2 原图把反光 / 阴影画进了颜色，像碎玻璃），
# 用户说“头盔贴图错误”。明暗图（灰度，线性）× 部件底色 → 只在 part0 / part1 的 UV 区域里压掉偏暗的斑块、收窄明暗起伏；
# 骷髅、角、狮子等靠原图明暗出形体的部件不动。原图在 ArtSource/Cover/helm3_backup_1111/，每次从原图重算（可重复跑）。
# 先跑 helm3_fix1111.py（导出 /tmp/sp2/helm_uv.npz），再：/Library/Developer/CommandLineTools/usr/bin/python3 Tools/helm_retex/helm3_detail_fix.py
import numpy as np, cv2
W='/Users/ld/Documents/ChatGPT/WarTable/'
src=cv2.imread(W+'ArtSource/Cover/helm3_backup_1111/helm3_albedo.png',cv2.IMREAD_UNCHANGED).astype(np.float32)/255
S=src.shape[0]; uv=np.load('/tmp/sp2/helm_uv.npz')
ID=np.full((S,S),255,np.uint8)
for k in range(7):   # part7 内衬不用贴图，它的 UV 是乱的，画进来会盖掉别的部件
    t=uv.get(f'part{k}')
    if t is None: continue
    pts=np.stack([t[...,0]*S,(1-t[...,1])*S],-1)
    cv2.fillPoly(ID,list(np.round(pts*4).astype(np.int32)),k,lineType=cv2.LINE_8,shift=2)
# 部件边缘外扩几像素（mip 采样时不渗进没处理的原图）
for _ in range(6):
    hole=ID==255
    dil=cv2.dilate(np.where(hole,0,ID.astype(np.int32)+1).astype(np.uint8),np.ones((3,3),np.uint8))
    ID=np.where(hole&(dil>0),dil-1,ID).astype(np.uint8)
out=src.copy()
# 部件：(下限、上限（相对中位）、保留多少起伏)
rule={0:(0.86,1.18,0.45),1:(0.8,1.25,0.6)}
for k,(lo,hi,g) in rule.items():
    m=ID==k
    med=float(np.median(src[m]))
    r=np.clip(src/med,lo,hi)**g
    # 大尺度斑块再抹平：减去 31 px 模糊后的低频起伏的一半
    blur=cv2.GaussianBlur(r,(0,0),12)
    r=r/np.power(blur,0.5)
    v=med*r[m]
    v*=float(src[m].mean())/max(float(v.mean()),1e-4)   # 整体亮度保持和原图一样（只去斑、不提亮，免得过曝）
    out[m]=np.clip(v,0,1)
    print(f'part{k} px={m.sum()} median={med:.3f} before p5={np.percentile(src[m],5):.3f} after p5={np.percentile(out[m],5):.3f}')
cv2.imwrite(W+'Assets/_WarTable/Art/Models/Cover/Helm3/helm3_albedo.png',(out*255+0.5).astype(np.uint8))
print('ok')

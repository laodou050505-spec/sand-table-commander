import numpy as np, cv2
d=np.load('faces.npz'); uv,pos=d['uv'],d['pos']; lab=np.load('flab.npy')
P=pos.mean(1); X,Y,Z=P.T
cnt=np.bincount(lab)
# 材质：0 银钢 1 黑铁 2 鎏金 3 角 4 骨 5 铆钉（骑士侧黄铜）6 铆钉（兽人侧古铜）
mid=np.where(Y<0,0,1)                       # 外壳：骑士半边（−Y）银钢，兽人半边黑铁
mid[np.isin(lab,[24,2,21])]=2               # 狮子、下沿金边、面甲横梁
mid[np.isin(lab,[70,28])]=3                 # 两只角
mid[lab==11]=4                              # 骷髅
mid[lab==1]=0                               # 骑士面甲
mid[np.isin(lab,[77,41])]=1                 # 角根铁箍
small=cnt[lab]<420
mid[small&~np.isin(lab,[2,21])]=np.where(Y[small&~np.isin(lab,[2,21])]<0,5,6)
print('faces per mat',np.bincount(mid,minlength=7))
S=2048
A=cv2.imread('/Users/ld/Documents/ChatGPT/WarTable/Assets/_WarTable/Art/Models/Cover/Helm3/helm3_albedo.png')[...,::-1].astype(np.float32)/255
ID=np.full((S,S),255,np.uint8)
pts=np.stack([uv[...,0]*S,(1-uv[...,1])*S],-1)
for k in range(7):
    m=mid==k
    if m.any(): cv2.fillPoly(ID,[p for p in np.round(pts[m]*4).astype(np.int32)],int(k),lineType=cv2.LINE_8,shift=2)
# 缝隙：用最近的已分配像素填（避免 mip 时渗出别的颜色）
for _ in range(12):
    hole=ID==255
    if not hole.any(): break
    dil=cv2.dilate(np.where(hole,0,ID.astype(np.int32)+1).astype(np.uint8),np.ones((3,3),np.uint8))
    ID=np.where(hole&(dil>0),dil-1,ID).astype(np.uint8)
ID[ID==255]=1
L=A.mean(2)
pal=np.array([[0.80,0.81,0.84],[0.20,0.195,0.19],[0.88,0.67,0.30],[0.66,0.55,0.40],[0.88,0.82,0.70],[0.80,0.62,0.30],[0.48,0.33,0.20]],np.float32)
metal=np.array([1.0,0.85,1.0,0.0,0.0,1.0,0.9],np.float32)
smooth=np.array([0.62,0.36,0.58,0.32,0.28,0.55,0.42],np.float32)
gain=np.array([0.55,0.7,0.8,1.0,1.1,0.6,0.6],np.float32)   # 原图明暗保留多少（骷髅、角、狮子靠它出形体）
out=np.zeros((S,S,3),np.float32); mr=np.zeros((S,S,4),np.float32)
for k in range(7):
    m=ID==k
    if not m.any(): continue
    med=np.median(L[m]); det=np.clip(L[m]/max(med,1e-3),0.45,1.6)**gain[k]
    out[m]=np.clip(pal[k][None]*det[:,None],0,1)
    mr[m,0]=metal[k]; mr[m,3]=np.clip(smooth[k]*(0.85+0.15*det),0,1)
cv2.imwrite('helm3_albedo2.png',(out[...,::-1]*255+0.5).astype(np.uint8))
cv2.imwrite('helm3_metal.png',(mr[...,[2,1,0,3]]*255+0.5).astype(np.uint8))
prev=cv2.resize(out,(900,900))[...,::-1]; cv2.imwrite('/tmp/wt_fix/h3_newalb.png',(prev*255).astype(np.uint8))
print('ok')

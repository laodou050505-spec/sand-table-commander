import sys; sys.path.insert(0,'/tmp/helm'); from common import *
from scipy import ndimage as ndi
s=cv2.imread('src_albedo_srgb.png')[...,::-1].astype(np.float32)/255
p=rd('pos',False); n=rd('nrm',False); valid=np.linalg.norm(n,axis=2)>0.5
cen=np.load('cluster_centers.npy')
H,W=valid.shape
lab=np.full((H,W),-1,np.int32)
lab[valid]=((s[valid][:,None,:]-cen[None])**2).sum(2).argmin(1)
# 类：0 银钢 1 黑铁 2 鎏金 3 角 4 骨 5 古铜铆钉
cmap=np.array([2,1,5,1,0,5,1,2])     # red green blue yellow magenta cyan orange brown
mid=np.full((H,W),-1,np.int32); mid[valid]=cmap[lab[valid]]
X,Y,Z=p[...,0],p[...,1],p[...,2]
hornish=np.isin(lab,[2,5])
# 角：蓝/青的连通块里面积大的（铆钉是小圆点）
hl,nh=ndi.label(cv2.morphologyEx(hornish.astype(np.uint8),cv2.MORPH_CLOSE,np.ones((3,3),np.uint8)))
area=ndi.sum(np.ones_like(valid),hl,range(1,nh+1))
big=np.zeros(nh+1,bool); big[1:]=area>1500
horn=big[hl]&valid&(Y>0.12)&(X<0.12)
# 角岛上的暗纹也算角：岛内角占多数则整岛为角
isl,nis=ndi.label(valid)
cnt=ndi.sum(np.ones_like(valid),isl,range(1,nis+1)); hc=ndi.sum(horn,isl,range(1,nis+1))
hi=np.zeros(nis+1,bool); hi[1:]=hc/np.maximum(cnt,1)>0.5
horn|=hi[isl]&valid
mid[horn]=3
# 骨：兽人半边（y>0.08）、下半部的亮色
skull=(Y>0.01)&(Z<0.15)&(Z>-0.47)&(X>0.0)
bone=valid&~horn&skull&(np.isin(lab,[0,4,7])|(hornish&(Z<-0.2)))
mid[bone]=4
# 狮子：顶上全是鎏金
mid[valid&(Z>0.3)]=2
# 去噪：众数滤波（5x5），只在有效区
oh=np.stack([(mid==k).astype(np.float32) for k in range(6)],-1)
oh=np.stack([cv2.blur(oh[...,k],(5,5)) for k in range(6)],-1)
mid2=np.where(valid,oh.argmax(-1),-1)
# 角岛内保持角
mid2[horn]=3
np.save('matid.npy',mid2)
pal=np.array([[200,200,210],[40,40,45],[230,180,60],[150,110,70],[235,225,200],[180,90,40]],np.uint8)
img=np.zeros((H,W,3),np.uint8); img[valid]=pal[mid2[valid]]
cv2.imwrite('matid.png',img[...,::-1])
print('islands',nis,'horn px',horn.sum(),'frac',[round((mid2==k).sum()/valid.sum(),3) for k in range(6)])

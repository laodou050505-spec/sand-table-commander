"""按材质分区重画战盔 PBR 贴图（UV0，2048）：albedo（sRGB）、metal（R 金属度 / A 光滑度）、normal（OpenGL）、occlusion。"""
import sys; sys.path.insert(0,'/tmp/helm'); from common import *
from scipy import ndimage as ndi
N=2048
src=cv2.imread('src_albedo_srgb.png')[...,::-1].astype(np.float32)/255
P=rd('pos',False); nrm=rd('nrm',False); valid=np.linalg.norm(nrm,axis=2)>0.5
ao=rd('ao',False)[...,0]; pt=rd('point',False)[...,0]
mid=np.load('matid.npy')
# 骷髅框里的小块“骨”其实是铆钉
bl,nb=ndi.label(mid==4); ar=ndi.sum(np.ones_like(valid),bl,range(1,nb+1))
small=np.zeros(nb+1,bool); small[1:]=ar<250; mid[small[bl]]=5
# 骷髅框里的金 / 古铜（眼眶里的金条纹、獠牙）都归骨；小圆点仍是铆钉
X0,Y0,Z0=P[...,0],P[...,1],P[...,2]
skull=(Y0>0.01)&(Z0<0.15)&(Z0>-0.47)&(X0>0.0)&valid
gb=skull&np.isin(mid,[2,5])
gl,ng=ndi.label(gb); ga=ndi.sum(np.ones_like(valid),gl,range(1,ng+1)); big=np.zeros(ng+1,bool); big[1:]=ga>=250
mid[big[gl]]=4
# 眼窝、鼻腔这些凹进去的地方做成暗铁（空洞感），其余骷髅里的金 / 古铜都归骨
rec=skull&(ao<0.45)&(mid!=4)&(mid!=3)
mid[skull&(ao>=0.45)&np.isin(mid,[2])]=4
# P2 原图在骷髅上画了阴影，被分成黑铁 / 鎏金的斑块：按 xatlas 的 UV 块整块投票——骷髅和铁板几何上分属不同块，
# 块里骨占三成以上、且整块在骷髅附近，就整块归骨（铆钉小圆点、深凹的眼窝除外）
ch=np.load('/tmp/helm/charts.npy')
_,(iy,ix)=ndi.distance_transform_edt(ch==0,return_indices=True); chf=ch[iy,ix]   # 外扩边距归到最近的块
near=(P[...,1]>-0.04)&(P[...,0]>-0.02)&(P[...,2]<0.17)&(P[...,2]>-0.49)
nc=ch.max()+1
tot=np.bincount(ch.ravel(),minlength=nc).astype(np.float32)
bf=np.bincount(ch.ravel(),weights=(mid==4).ravel(),minlength=nc)/np.maximum(tot,1)
nf=np.bincount(ch.ravel(),weights=near.ravel(),minlength=nc)/np.maximum(tot,1)
bonech=(bf>0.3)&(nf>0.8); bonech[0]=False
rv=(mid==5)
rl,nr=ndi.label(rv); ra=ndi.sum(np.ones_like(valid),rl,range(1,nr+1)); sr=np.zeros(nr+1,bool); sr[1:]=ra<250
keep_rivet=sr[rl]
sel=bonech[chf]&valid&~keep_rivet&(mid!=3)
mid[sel]=4
print('bone charts', int(bonech.sum()))
# 骷髅前面残留着一层背面朝外的隐藏面（P2 产物），AO 在骷髅上全是黑的：骨的 AO 设下限，凹缝改用凸度压暗
ao=np.where(mid==4,np.maximum(ao,0.8),ao)
# 角尖：角那一侧伸出去的部分（Y>0.24）不是铁就是角
mid[valid&(P[...,1]>0.24)&np.isin(mid,[0,2,4,5])]=3
np.save('matid_final.npy',mid)

rng=np.random.default_rng(7)
def vnoise(p, freq, seed):
    g=np.random.default_rng(seed).random((64,64,64)).astype(np.float32)
    q=p*freq; i=np.floor(q).astype(np.int64); f=q-i; f=f*f*(3-2*f)
    out=0
    for dx in (0,1):
        for dy in (0,1):
            for dz in (0,1):
                w=(f[...,0] if dx else 1-f[...,0])*(f[...,1] if dy else 1-f[...,1])*(f[...,2] if dz else 1-f[...,2])
                out=out+w*g[(i[...,0]+dx)%64,(i[...,1]+dy)%64,(i[...,2]+dz)%64]
    return out
def fbm(p, f0, oct, seed, gain=0.5):
    s=0; a=1; t=0
    for o in range(oct): s=s+a*vnoise(p,f0*2**o,seed+o); t+=a; a*=gain
    return s/t
Pv=P[valid]
def field(fn):
    o=np.zeros((N,N),np.float32); o[valid]=fn(Pv); return o
n_low=field(lambda p: fbm(p,4,3,1))            # 大块色差
n_mid=field(lambda p: fbm(p,18,3,11))          # 锻打颗粒
n_fine=field(lambda p: fbm(p,80,2,21))         # 细颗粒
brush=field(lambda p: fbm(p*np.array([1,1,60],np.float32),3,3,31))   # 竖向拉丝（沿高度方向的细纹）
def rot(p): return np.stack([(0.8*p[:,0]-0.6*p[:,1])*90,(0.6*p[:,0]+0.8*p[:,1])*90,p[:,2]*4],-1)
scr=field(lambda p: fbm(rot(p),1,2,41))
scratch=np.clip((scr-0.62)/0.08,0,1)           # 稀疏划痕
cav=np.clip(1-ao,0,1)                           # 凹处
edge=np.clip((pt-0.585)/0.03,0,1)                # 凸边
lum=src.mean(-1)
shade=np.clip(cv2.GaussianBlur(lum,(0,0),1.2)/0.5,0.12,1.5)   # P2 原图画进去的明暗
def hp(x, s): return x-cv2.GaussianBlur(x,(0,0),s)

M={k:(mid==k)&valid for k in range(6)}
alb=np.zeros((N,N,3),np.float32); met=np.zeros((N,N),np.float32); smo=np.zeros((N,N),np.float32); hgt=np.zeros((N,N),np.float32)
def put(m, c, me, sm, h):
    alb[m]=c[m] if c.ndim==3 else c; met[m]=me[m] if np.ndim(me)==2 else me; smo[m]=sm[m] if np.ndim(sm)==2 else sm; hgt[m]=h[m] if np.ndim(h)==2 else h
one=np.ones((N,N),np.float32)
def lin3(r,g,b): return np.array([r,g,b],np.float32)
# 0 银钢：抛光钢、竖向拉丝、少量细划痕、凹处微暖暗
steel=lin3(0.50,0.51,0.53)[None,None]*(0.92+0.12*n_low[...,None])*(1-0.35*cav[...,None])
put(M[0], steel, one, 0.70+0.10*n_low-0.04*brush-0.25*cav, 0.12*brush+0.15*n_low)
# 1 黑铁：发蓝的熟铁，锻打颗粒，凸边磨出钢色
iron=lin3(0.055,0.058,0.064)[None,None]*(0.8+0.5*n_mid[...,None])
iron=iron*(1-0.6*edge[...,None])+lin3(0.28,0.28,0.29)[None,None]*0.6*edge[...,None]
iron=iron*(1-0.4*cav[...,None])
put(M[1], iron, 0.9*one, 0.40+0.15*n_mid+0.2*edge-0.15*cav, 0.6*n_mid+0.25*n_fine)
# 2 鎏金：暖金，凹处沉成古铜，凸边擦亮
gold=lin3(0.78,0.55,0.24)[None,None]*(0.9+0.15*n_low[...,None])
gold=gold*(1-0.55*cav[...,None])+lin3(0.25,0.14,0.05)[None,None]*0.55*cav[...,None]
gold=gold*(1-0.3*edge[...,None])+lin3(0.98,0.80,0.45)[None,None]*0.3*edge[...,None]
lionm=P[...,2]>0.31
gold=np.where(lionm[...,None], gold*(0.35+0.65*shade[...,None]), gold)
put(M[2], gold, one, np.where(P[...,2]>0.31,0.4,0.66)+0.12*edge-0.3*cav+0.06*n_mid, np.where(P[...,2]>0.31,0,0.12)*n_mid)
# 3 角：保留原图的角纹（亮度高通），换成干净的象牙到深褐的渐变
hv=np.clip(0.5+2.2*hp(lum,6),0,1)
tip=np.clip((-P[...,0]-0.15)/0.18,0,1)        # 越往后（角尖）越深
horn_c=(lin3(0.15,0.105,0.065)*(1-tip[...,None])+lin3(0.045,0.032,0.024)*tip[...,None])*(0.55+0.9*hv[...,None])
horn_c=horn_c*(1-0.35*cav[...,None])
put(M[3], horn_c, 0*one, 0.38+0.15*hv-0.15*cav, 2.5*hp(lum,4))
# 4 骷髅：铸造古铜浮雕（P2 的骷髅几何很乱，骨色会把乱处全暴露；深色古铜藏得住，也和黑铁半边一个调子）
conc=np.clip((0.5-pt)/0.04,0,1)
bz=lin3(0.30,0.20,0.10)[None,None]*(0.75+0.35*n_mid[...,None])
bz=bz*(1-0.7*conc[...,None])+lin3(0.03,0.022,0.015)[None,None]*0.7*conc[...,None]
bz=bz*(1-0.35*edge[...,None])+lin3(0.62,0.45,0.24)[None,None]*0.35*edge[...,None]
bz=bz*shade[...,None]
put(M[4], bz, one, 0.42+0.15*edge-0.15*conc+0.08*n_mid, 0.5*n_mid)
# 5 古铜铆钉
bro=lin3(0.45,0.30,0.15)[None,None]*(1-0.5*cav[...,None])
put(M[5], bro, one, 0.58+0.2*edge-0.2*cav, 0*one)
# 分区边界羽化 1.5 px（不出硬锯齿）
for arr in (alb,):
    for c in range(3): arr[...,c]=np.where(valid, cv2.GaussianBlur(arr[...,c],(0,0),0.8), 0)
met=np.where(valid,cv2.GaussianBlur(met,(0,0),0.8),0); smo=np.clip(np.where(valid,cv2.GaussianBlur(smo,(0,0),0.8),0),0.05,0.95)
# 高度 → 法线（UV 空间；切线 = +U，副切线 = +V，图像第 0 行 = V=1）
er=cv2.erode(valid.astype(np.uint8),np.ones((5,5),np.uint8)).astype(bool)
h=cv2.GaussianBlur(hgt,(0,0),1.0)
gx=cv2.Sobel(h,cv2.CV_32F,1,0,ksize=3)/8; gy=-cv2.Sobel(h,cv2.CV_32F,0,1,ksize=3)/8
k=float(sys.argv[1]) if len(sys.argv)>1 else 1.5
nx=-gx*k; ny=-gy*k; nz=np.ones_like(nx); L=np.sqrt(nx*nx+ny*ny+1)
nmap=np.stack([nx/L,ny/L,nz/L],-1); nmap[~er]=[0,0,1]
def dilate(img, m, it=12):
    img=img.copy(); m=m.copy()
    for _ in range(it):
        acc=cv2.blur(img*m[...,None] if img.ndim==3 else img*m,(3,3)); w=cv2.blur(m.astype(np.float32),(3,3))
        new=(w>0)&~m
        if img.ndim==3: img[new]=(acc[new]/w[new][:,None])
        else: img[new]=acc[new]/w[new]
        m=m|new
    return img
alb=dilate(alb,valid); met=dilate(met,valid); smo=dilate(smo,valid); occ=dilate(np.clip(0.25+0.75*ao,0,1),valid)
def srgb(x): return np.where(x<=0.0031308,x*12.92,1.055*np.power(np.clip(x,0,1),1/2.4)-0.055)
O='/tmp/helm/out'; import os; os.makedirs(O,exist_ok=True)
cv2.imwrite(f'{O}/helm_albedo.png',(np.clip(srgb(alb),0,1)*255+0.5).astype(np.uint8)[...,::-1])
cv2.imwrite(f'{O}/helm_metal.png',(np.dstack([met,met,met,smo])*255+0.5).astype(np.uint8))   # BGRA：RGB=金属度，A=光滑度
cv2.imwrite(f'{O}/helm_normal.png',((nmap*0.5+0.5)*255+0.5).astype(np.uint8)[...,::-1])
cv2.imwrite(f'{O}/helm_ao.png',(occ*255+0.5).astype(np.uint8))
print('ok')

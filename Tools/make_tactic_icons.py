"""兵法页示意图（11-07 用户：图标要好看）：Tripo 水墨生成图 → 抠出墨迹 → 和手势图标同一套样式（深墨圆斑底 + 骨白笔画）。
python3 Tools/make_tactic_icons.py → ArtSource/Cover/v6/alpha/tac_<名>.png（512×512，带透明）"""
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage as ndi
R='/Users/ld/Documents/ChatGPT/WarTable/'
A=R+'ArtSource/Cover/v6/alpha/'; G=R+'ArtSource/UI/gen/'
SRC={'anvil':'tac_anvil2','focus':'tac_focus','general':'tac_general','wall':'tac_wall'}
rng=np.random.default_rng(3); S=512
yy,xx=np.mgrid[0:S,0:S]; cx=cy=S/2; ang=np.arctan2(yy-cy,xx-cx); dd=np.hypot(xx-cx,yy-cy)/(S*0.46)
edge=1+0.06*np.sin(ang*5+1.3)+0.04*np.sin(ang*11)
blot=np.clip((edge-dd)/0.06,0,1)
noise=np.array(Image.fromarray((rng.random((S//8,S//8))*255).astype(np.uint8)).resize((S,S),Image.BICUBIC)).astype(float)/255
blot*=0.82+0.12*noise
import os
for name,folder in SRC.items():
    p=G+folder+'/01_generated_image_url.png'
    if not os.path.exists(p): print('skip',name); continue
    src=np.array(Image.open(p).convert('L')).astype(float)
    paper=np.percentile(src,80)
    ink=np.clip((paper-src)/max(1,paper-src.min()),0,1); ink=np.clip((ink-0.15)/0.85,0,1)
    lab,nl=ndi.label(ink>0.2)
    if nl:
        sizes=ndi.sum(np.ones_like(ink),lab,range(1,nl+1))
        for li,sz in enumerate(sizes,1):
            if sz<src.size*0.0004: ink[lab==li]=0   # 纸纹小斑点
    ys,xs=np.where(ink>0.5); H0,W0=ink.shape; pad=int(max(H0,W0)*0.02)
    ink=ink[max(0,ys.min()-pad):min(H0,ys.max()+pad+1), max(0,xs.min()-pad):min(W0,xs.max()+pad+1)]
    ink[ink<0.12]=0
    h,w=ink.shape; k=int(S*0.62)/max(h,w)
    im=Image.fromarray((ink*255).astype(np.uint8)).resize((max(1,int(w*k)),max(1,int(h*k))),Image.LANCZOS)
    # 细笔画（断旗）缩小后看不清 → 按墨量自动加粗：墨占比越少，膨胀越多
    cover=(np.array(im)>80).mean()
    if cover<0.12: im=im.filter(ImageFilter.MaxFilter(7 if cover<0.06 else 5))
    fig=np.zeros((S,S)); oy=(S-im.height)//2; ox=(S-im.width)//2
    fig[oy:oy+im.height, ox:ox+im.width]=np.array(im).astype(float)/255
    fig=np.clip(fig*1.4,0,1)
    bone=np.array([0.96,0.92,0.82]); dark=np.array([0.06,0.05,0.045])
    a=np.clip(blot+fig*(1-blot),0,1)
    rgb=(dark*(blot*(1-fig))[...,None]+bone*fig[...,None])/np.maximum(a[...,None],1e-4)
    out=Image.fromarray((np.dstack([rgb,a])*255).astype(np.uint8))
    out.putalpha(out.split()[3].filter(ImageFilter.GaussianBlur(2.5)))
    out.save(A+f'tac_{name}.png'); print(name,'ok')

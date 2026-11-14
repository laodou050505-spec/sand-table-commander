"""教学提示条（墨笔刷底 + 霞鹜文楷 / Cinzel）和手势图标（深墨圆底 + 骨白手形）。
python3 Tools/make_tutorial_tips.py  → ArtSource/Cover/v6/alpha/tip_<步>_<zh|en>.png、hand_*.png"""
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
R='/Users/ld/Documents/ChatGPT/WarTable/'
zh=ImageFont.truetype(R+'ArtSource/Fonts/LXGWWenKai-Regular.ttf',92)
en=ImageFont.truetype(R+'ArtSource/Fonts/Cinzel.ttf',60)
try: en.set_variation_by_axes([700])
except Exception: pass
brush=Image.open(R+'ArtSource/Cover/v6/alpha/brush_1.png').convert('RGBA')
A=R+'ArtSource/Cover/v6/alpha/'
# 11-09 用户：教程要讲清楚“什么能拖、拖到哪、拖到敌军上会怎样”，不说“可不做”。
# 每条两行：第一行 = 手怎么做（大字），第二行 = 会发生什么（小一号、金色）。键 = Tutorial.Step 的小写名
# 11-10：软提示（键 = Tutorial.Keys）。像教傻瓜一样：第一行只说手怎么做，第二行说会发生什么
tips={
 'palm':(('手掌平放在桌上，停一秒','沙盘会摆在你正前方的桌面上'),('Rest your palm flat on the table for a second','The board appears right in front of you')),
 'adjust':(('捏住木框上的圆钮，把沙盘拖到舒服的位置','两只手各捏一个还能转动、缩放；摆好了点一下“放好了”'),('Pinch a knob on the frame and drag the board','Two hands turn and resize it · tap READY when it feels right')),
 'move':(('伸手捏住一块蓝色令牌，拖到别处松手','你的部队就会走过去'),('Pinch a blue token and drop it somewhere','Your troops march there')),
 'banner':(('准备好了？捏住身边的蓝色帅旗，停一下','金圈转满就开战'),('Ready? Pinch and hold the blue banner beside you','When the ring fills, the battle begins')),
 'attack':(('把蓝色令牌拖到一块红色令牌上松手','这支部队就去打那支敌军'),('Drop a blue token on a red token','That unit attacks the enemy')),
 # 11-10 用户：去掉手势 → 沙盘右侧令架上的三支令牌，扔到沙盘上任意位置 = 对全军下令
 'charge':(('从沙盘右边捏起“冲锋”令，扔到沙盘上','全军冲锋；另外两支令牌用法一样'),('Pick up the CHARGE token on your right, toss it on the board','Everyone charges. The other tokens work the same way')),
 'hold':(('捏起“坚守”令，扔到沙盘上任意位置','全军坚守，原地站住等敌人上来'),('Toss the HOLD token anywhere on the board','Hold: everyone stands their ground')),
 'retreat':(('捏起“撤退”令，扔到沙盘上任意位置','全军撤退，往你这边退'),('Toss the RETREAT token anywhere on the board','Retreat: everyone falls back toward you')),
 'hands':(('把手抬到眼前','头显要看得到你的手才能操作'),('Raise your hands in front of you','The headset needs to see your hands')),
}
learn={'move':('移动部队','Moving troops'),'banner':('开战','Starting the battle'),'attack':('进攻敌军','Attacking'),
       'charge':('冲锋','Charge'),'hold':('坚守','Hold'),'retreat':('撤退','Retreat')}
zh2=zh.font_variant(size=64); en2=en.font_variant(size=44)
def fit(d,txt,font,maxw):
    tw=d.textlength(txt,font=font)
    if tw>maxw: font=font.font_variant(size=int(font.size*maxw/tw)); tw=d.textlength(txt,font=font)
    return font,tw
for k,(z,e) in tips.items():
    for lang,(l1,l2),f1,f2 in [('zh',z,zh,zh2),('en',e,en,en2)]:
        W,H=1700,340
        im=Image.new('RGBA',(W,H),(0,0,0,0))
        # 柔边深墨底托住两行字（笔刷边缘毛糙，单靠笔刷会吃字），上面再叠笔刷做边缘的手作感
        base=Image.new('L',(W,H),0); ImageDraw.Draw(base).rounded_rectangle((90,40,W-90,H-40),radius=70,fill=225)
        base=base.filter(ImageFilter.GaussianBlur(26))
        im.alpha_composite(Image.merge('RGBA',(Image.new('L',(W,H),14),Image.new('L',(W,H),12),Image.new('L',(W,H),10),base)))
        # 两道墨笔刷，各托一行字
        for cy,hh,ww in ((0.36,0.62,1.0),(0.7,0.5,0.86)):
            bw=int(W*ww); b=brush.resize((bw,int(H*hh*1.6)),Image.LANCZOS)
            ba=np.array(b).astype(float); ba[...,:3]=np.array([14,12,10]); ba[...,3]*=0.94
            im.alpha_composite(Image.fromarray(ba.astype(np.uint8)),((W-bw)//2,int(H*cy-H*hh*0.8)))
        d=ImageDraw.Draw(im)
        a,tw=fit(d,l1,f1,W*0.8); d.text((W/2,H*0.36),l1,font=a,fill=(240,231,210,255),anchor='mm')
        c,tw=fit(d,l2,f2,W*0.8); d.text((W/2,H*0.7),l2,font=c,fill=(240,196,112,255),anchor='mm')
        im.save(A+f'tip_{k}_{lang}.png')
# 学会了：短一些的条，左边一枚朱漆圆印（白色对勾），右边“学会了”（金）+ 学会的是什么（大字）
for k,(z,e) in learn.items():
    for lang,what,head,f1,f2 in [('zh',z,'学会了',zh2,zh),('en',e,'LEARNED',en2,en)]:
        W,H=1300,300
        im=Image.new('RGBA',(W,H),(0,0,0,0))
        base=Image.new('L',(W,H),0); ImageDraw.Draw(base).rounded_rectangle((60,40,W-60,H-40),radius=70,fill=230)
        base=base.filter(ImageFilter.GaussianBlur(24))
        im.alpha_composite(Image.merge('RGBA',(Image.new('L',(W,H),14),Image.new('L',(W,H),12),Image.new('L',(W,H),10),base)))
        d=ImageDraw.Draw(im); cx,cy,r=190,H//2,92
        d.ellipse((cx-r,cy-r,cx+r,cy+r),fill=(158,32,24,255),outline=(236,196,112,255),width=7)
        d.line([(cx-44,cy+2),(cx-12,cy+36),(cx+48,cy-34)],fill=(250,238,214,255),width=20,joint='curve')
        d.text((330,H*0.33),head,font=f1,fill=(240,196,112,255),anchor='lm')
        a2,_=fit(d,what,f2,W-400); d.text((330,H*0.66),what,font=a2,fill=(244,236,216,255),anchor='lm')
        im.save(A+f'learn_{k}_{lang}.png')
print('tips',len(tips))
rng=np.random.default_rng(3); S=512
yy,xx=np.mgrid[0:S,0:S]; cx=cy=S/2; ang=np.arctan2(yy-cy,xx-cx); dd=np.hypot(xx-cx,yy-cy)/(S*0.46)
edge=1+0.06*np.sin(ang*5+1.3)+0.04*np.sin(ang*11)
blot=np.clip((edge-dd)/0.06,0,1)
noise=np.array(Image.fromarray((rng.random((S//8,S//8))*255).astype(np.uint8)).resize((S,S),Image.BICUBIC)).astype(float)/255
blot*=0.82+0.12*noise
for n in ['hand_tray','hand_fist','hand_press','hand_beckon']:
    src=np.array(Image.open(R+f'ArtSource/UI/gen/{n}.png').convert('L')).astype(float)
    # 手工框（原图像素 x0, y0, x1, y1）：握拳那张纸下沿的阴影和墨色太像，自动裁不掉
    box={'hand_fist':(190,640,1940,1380)}.get(n)
    if box: src=src[box[1]:box[3], box[0]:box[2]]
    # 有的生成图是一张纸放在灰底上：先裁到纸面里面（纸边的阴影会变成一道线）
    m=src>np.percentile(src,90)-20
    rows=np.where(m.mean(1)>0.6)[0]; cols=np.where(m.mean(0)>0.6)[0]
    if len(rows) and len(cols):
        y0,y1,x0,x1=rows[0],rows[-1],cols[0],cols[-1]; dy=int((y1-y0)*0.03); dx=int((x1-x0)*0.03)
        src=src[y0+dy:y1-dy, x0+dx:x1-dx]
    paper=np.percentile(src,90)
    ink=np.clip((paper-src)/max(1,paper-src.min()),0,1); ink=np.clip((ink-0.12)/0.88,0,1)
    ys,xs=np.where(ink>0.15); ink=ink[ys.min():ys.max()+1, xs.min():xs.max()+1]
    # 去掉又扁又长的细线（生成图里的纸边 / 桌沿），保留手和箭头
    from scipy import ndimage as ndi
    lab,nl=ndi.label(ink>0.15)
    for li,sl in enumerate(ndi.find_objects(lab),1):
        hh=sl[0].stop-sl[0].start; ww=sl[1].stop-sl[1].start
        if hh<ink.shape[0]*0.03 and ww>ink.shape[1]*0.2: ink[lab==li]=0
    # 按浓墨（>0.5）的范围裁，再留 3% 边：纸边阴影这类淡线不会把裁切框撑大；淡于 0.12 的一律清掉
    ys,xs=np.where(ink>0.5); H0,W0=ink.shape; py=int(H0*0.03); px=int(W0*0.03)
    ink=ink[max(0,ys.min()-py):min(H0,ys.max()+py+1), max(0,xs.min()-px):min(W0,xs.max()+px+1)]
    ink[ink<0.12]=0
    h,w=ink.shape; k=int(S*0.66)/max(h,w)
    im=Image.fromarray((ink*255).astype(np.uint8)).resize((max(1,int(w*k)),max(1,int(h*k))),Image.LANCZOS).filter(ImageFilter.MaxFilter(3))
    hand=np.zeros((S,S)); oy=(S-im.height)//2; ox=(S-im.width)//2
    hand[oy:oy+im.height, ox:ox+im.width]=np.array(im).astype(float)/255
    hand=np.clip(hand*1.5,0,1)
    bone=np.array([0.96,0.92,0.82]); dark=np.array([0.06,0.05,0.045])
    a=np.clip(blot+hand*(1-blot),0,1)
    rgb=(dark*(blot*(1-hand))[...,None]+bone*hand[...,None])/np.maximum(a[...,None],1e-4)
    out=Image.fromarray((np.dstack([rgb,a])*255).astype(np.uint8))
    al=out.split()[3].filter(ImageFilter.GaussianBlur(2.5)); out.putalpha(al)
    out.save(A+f'{n}.png'); print(n,'ok')
c=Image.new('RGB',(1600,400),(150,145,138))
for i,n in enumerate(['hand_tray','hand_fist','hand_press','hand_beckon']):
    im=Image.open(A+n+'.png').resize((400,400)); c.paste(im,(i*400,0),im)
c.save('/tmp/icons4_preview.png')   # 预览图不进工程

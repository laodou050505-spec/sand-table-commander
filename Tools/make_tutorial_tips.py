"""教学提示条（墨笔刷底 + 霞鹜文楷 / Cinzel）和手势图标（深墨圆底 + 骨白手形）。
python3 Tools/make_tutorial_tips.py  → ArtSource/Cover/v6/alpha/tip_<步>_<zh|en>.png、hand_*.png"""
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
R='/Users/ld/Documents/ChatGPT/WarTable/'
zh=ImageFont.truetype(R+'ArtSource/Fonts/SourceHanSerifCN-Bold.otf',92)
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
 'adjust':(('捏住边框上的圆钮，把沙盘挪到顺手的位置，高低皆可调','双手各捏一枚可旋转、缩放；调好后按下“就位”'),('Pinch a knob on the frame and move the board, up and down too','Two hands turn and resize it · press READY when it feels right')),   # 11-16：沙盘自己出现在面前，能连高度一起挪
 'move':(('捏住一面蓝色旗牌，拖到别处再松手','这支部队随即开拔'),('Pinch a blue token and drop it somewhere','Your troops march there')),
 'banner':(('部署完毕？按下面前的红色“开战”','战斗即刻打响'),('Ready? Press the red ENGAGE button in front of you','The battle begins at once')),   # 11-16：开战改成指挥台正中的大按钮
 'attack':(('把蓝色旗牌拖到一面红色旗牌上','这支部队便向那支敌军发起进攻'),('Drop a blue token on a red token','That unit attacks the enemy')),
 # 11-10 用户：去掉手势 → 沙盘右侧令架上的三支令牌，扔到沙盘上任意位置 = 对全军下令
 'charge':(('按下右手边的红色“冲锋”','全军冲锋；“坚守”“撤退”同理'),('Press the red CHARGE button on your right','The whole army charges. HOLD and RETREAT work the same way')),   # 11-16：令牌换成带拉环的令标
 'hold':(('按下蓝色“坚守”','全军原地列阵，以逸待劳'),('Press the blue HOLD button','The whole army stands its ground')),
 'retreat':(('按下琥珀色“撤退”','全军向己方阵后收拢'),('Press the amber RETREAT button','The whole army falls back toward you')),
 'hands':(('请把手抬到眼前','头显需要看到双手才能识别操作'),('Raise your hands in front of you','The headset needs to see your hands')),
}
learn={'move':('调遣部队','Moving troops'),'banner':('开战','Starting the battle'),'attack':('发起进攻','Attacking'),
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
        # 11-17 用户：界面不要规规矩矩、不要太死板、字不要太大 → 和结算板同一套拼贴：一条两头撕开的藏青纸（左端贴一小片象牙白纸签），
        #   字比原来小一号、从纸签右边起左对齐（底图由 Tools/make_collage_ui.py 出：ArtSource/UI/gen/collage/tip_bg.png）
        im.alpha_composite(Image.open(R+'ArtSource/UI/gen/collage/tip_bg.png').convert('RGBA'))
        d=ImageDraw.Draw(im); x0=250; mw=W-x0-110
        a,tw=fit(d,l1,f1.font_variant(size=int(f1.size*0.8)),mw); d.text((x0,H*0.385),l1,font=a,fill=(240,231,210,255),anchor='lm')
        c,tw=fit(d,l2,f2.font_variant(size=int(f2.size*0.8)),mw); d.text((x0,H*0.655),l2,font=c,fill=(226,188,116,255),anchor='lm')
        im.save(A+f'tip_{k}_{lang}.png')
# 学会了：短一些的条，左边一枚朱漆圆印（白色对勾），右边“学会了”（金）+ 学会的是什么（大字）
for k,(z,e) in learn.items():
    for lang,what,head,f1,f2 in [('zh',z,'已掌握',zh2,zh),('en',e,'LEARNED',en2,en)]:
        W,H=1300,300
        im=Image.new('RGBA',(W,H),(0,0,0,0))
        im.alpha_composite(Image.open(R+'ArtSource/UI/gen/collage/learn_bg.png').convert('RGBA'))                 # 同一套藏青纸条；左边压一枚小日轮纸片，上面一个金色对勾
        sun=Image.open(A+'fx_sun.png').convert('RGBA').resize((236,236),Image.LANCZOS); im.alpha_composite(sun,(72,H//2-118))
        d=ImageDraw.Draw(im); cx,cy=190,H//2
        d.ellipse((cx-58,cy-58,cx+58,cy+58),fill=(14,18,30,235))
        d.line([(cx-34,cy+2),(cx-9,cy+28),(cx+38,cy-27)],fill=(240,204,128,255),width=15,joint='curve')
        d.text((340,H*0.37),head,font=f1.font_variant(size=int(f1.size*0.72)),fill=(226,188,116,255),anchor='lm')
        a2,_=fit(d,what,f2.font_variant(size=int(f2.size*0.8)),W-460); d.text((340,H*0.63),what,font=a2,fill=(244,236,216,255),anchor='lm')
        im.save(A+f'learn_{k}_{lang}.png')
print('tips',len(tips))
rng=np.random.default_rng(3); S=512
yy,xx=np.mgrid[0:S,0:S]; cx=cy=S/2; ang=np.arctan2(yy-cy,xx-cx); dd=np.hypot(xx-cx,yy-cy)/(S*0.46)
edge=1+0.06*np.sin(ang*5+1.3)+0.04*np.sin(ang*11)
blot=np.clip((edge-dd)/0.06,0,1)
noise=np.array(Image.fromarray((rng.random((S//8,S//8))*255).astype(np.uint8)).resize((S,S),Image.BICUBIC)).astype(float)/255
blot*=0.82+0.12*noise
import os as _os2
for n in ['hand_tray','hand_fist','hand_press','hand_beckon']:
    if _os2.path.exists(R+f'ArtSource/UI/gen/{n}_e/01_generated_image_url.png'): continue      # 11-17：有铜版画重绘稿的由 Tools/make_hand_icons.py 出，这里不再覆盖
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

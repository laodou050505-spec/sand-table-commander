"""下令反馈牌（11-11 第二轮：手势已取消，金圈里不再画手势 → 画令牌上的同一个令徽（ArtSource/Tokens/emblems.json），圈底用该令牌的漆色。
原 11-07 用户：手势图标不要那么大、要高级、不要挡住画面；红色警告玩家看不懂）：
墨色漆牌 + 细金线框，左边金圈里是手势图标，右边一行字“谁 · 做什么”。
python3 Tools/make_order_badges.py → ArtSource/Cover/v6/alpha/order_<手势>_<谁>_<zh|en>.png"""
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
R='/Users/ld/Documents/ChatGPT/WarTable/'
A=R+'ArtSource/Cover/v6/alpha/'
F=R+'ArtSource/Fonts/'
zh=lambda s: ImageFont.truetype(F+'SourceHanSerifCN-Bold.otf',s)
def en(s):
    f=ImageFont.truetype(F+'Cinzel.ttf',s)
    try: f.set_variation_by_axes([600])
    except Exception: pass
    return f
GEST={'charge':('hand_fist','冲锋','CHARGE'),'hold':('hand_press','坚守','HOLD'),'retreat':('hand_beckon','撤退','RETREAT')}
WHO={'all':('全军','ALL'),'inf':('步兵','INFANTRY'),'arch':('弓兵','ARCHERS'),'cav':('骑兵','CAVALRY'),'ha':('骑射','HORSE ARCHERS'),'some':('所选部队','SELECTED')}
# 11-13：按风格各出一套（order_<令>_<兵种>_<后缀>_<语言>；中古那套和“全军 / 所选部队”文件名不变）
import os as _os, sys as _sys; _sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import era_names
import collage_kit as ck   # 11-17：全套界面换成拼贴纸片
for sfx in era_names.SUFFIXES[1:]:
    for g_ in ('inf','arch','cav','ha'): WHO[g_+era_names.tag(sfx)]=(era_names.ZH[sfx][g_],era_names.EN_LONG[sfx][g_])
GOLD=(206,168,96); W,H=760,200
import json
LACQ={'charge':(128,30,22),'hold':(30,62,112),'retreat':(140,92,24)}   # 11-17：和指挥台三个军令按钮的颜色一致（冲锋红 / 坚守蓝 / 撤退琥珀）
for g,(icon,gz,ge) in GEST.items():
    for w,(wz,we) in WHO.items():
        for lang in ('zh','en'):
            if lang=='zh': a,b,fa,fb=wz,gz,zh(42),zh(60)      # 11-17 用户：字不要太大（原 48 / 72）
            else: a,b,fa,fb=we,ge,en(30 if len(we)<10 else 24),en(48)
            md=ImageDraw.Draw(Image.new('RGBA',(8,8)))
            W=int(200+md.textlength(a,font=fa)+44+22+md.textlength(b,font=fb)+70)
            im=Image.new('RGBA',(W,H),(0,0,0,0))
            rgb_,a_,(x0,y0,x1,y1)=ck.real_strip(W-70,H-56,ck.NAVY,pad=12,cut='l',flip=len(w)%2==1)      # 真纸条（右端是撕的）比圆纸片矮一截，从圆纸片后面伸出来
            rgb_=ck.corner_dots(rgb_,9,rgb_.shape[1]*1.0,rgb_.shape[0]*1.0,rgb_.shape[1]*0.4,rgb_.shape[0]*1.3,(44,62,104),0.55)
            sl=ck.finish(rgb_,a_,12); im.alpha_composite(sl,(70,28)); d=ImageDraw.Draw(im)
            d.line([(210,28+y0+9),(70+x0+(x1-x0)*0.6,28+y0+9)],fill=ck.GOLD_D+(220,),width=2)
            d.line([(70+x0+(x1-x0)*0.45,28+y1-9),(70+x1-26,28+y1-9)],fill=ck.GOLD_D+(220,),width=2)
            ck.diamond(d,70+x1-26,28+y1-9,5,ck.GOLD+(255,))
            cx,cy,r=104,H//2,70
            im.alpha_composite(ck.ring_disc(200,LACQ[g],seed=3,field=0.58),(cx-100,cy-100)); d=ImageDraw.Draw(im)      # 边上一圈铜版画刻度
            # 令徽：和令牌上刻的同一张书法剪影（ArtSource/Tokens/emblem_<令>.png，11-12），等比缩进圈里 70% 的方框
            em=Image.open(R+f'ArtSource/Tokens/emblem_{g}.png').convert('L'); em=Image.eval(em,lambda v:255-v)
            k=(2*r*0.52)/max(em.size); em=em.resize((max(1,int(em.width*k)),max(1,int(em.height*k))),Image.LANCZOS)
            m=Image.new('L',(2*r,2*r),0); m.paste(em,(r-em.width//2,r-em.height//2))
            gl=Image.new('RGBA',(2*r,2*r),(236,190,104,255)); gl.putalpha(m)
            im.alpha_composite(gl,(cx-r,cy-r)); d=ImageDraw.Draw(im)
            # 字：谁（小、骨白）· 做什么（大、金）
            x=208
            tg=ck.chip(a,fa,padx=14,h=int(fa.size*1.5)+14,pad=7,cut='l',flip=True)      # “谁”写在一小片象牙白纸签上，斜贴着
            ck.paste_rot(im,tg,x+tg.width/2-4,cy-3,2.5); d=ImageDraw.Draw(im); x+=tg.width+10
            d.text((x,cy+2),b,font=fb,fill=(242,200,124,255),anchor='lm')
            im.save(A+f'order_{g}_{w}_{lang}.png')
W=900; prev=Image.new('RGBA',(W*2,H*3),(120,128,96,255))
for i,(g,w) in enumerate([('charge','all'),('hold','inf'),('retreat','cav')]):
    for j,l in enumerate(('zh','en')):
        t=Image.open(A+f'order_{g}_{w}_{l}.png'); prev.alpha_composite(t,(j*W,i*H))
prev.save('/tmp/sp2/order_prev.png'); print('badges ok')

"""下令反馈牌（11-11 第二轮：手势已取消，金圈里不再画手势 → 画令牌上的同一个令徽（ArtSource/Tokens/emblems.json），圈底用该令牌的漆色。
原 11-07 用户：手势图标不要那么大、要高级、不要挡住画面；红色警告玩家看不懂）：
墨色漆牌 + 细金线框，左边金圈里是手势图标，右边一行字“谁 · 做什么”。
python3 Tools/make_order_badges.py → ArtSource/Cover/v6/alpha/order_<手势>_<谁>_<zh|en>.png"""
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
R='/Users/ld/Documents/ChatGPT/WarTable/'
A=R+'ArtSource/Cover/v6/alpha/'
F=R+'ArtSource/Fonts/'
zh=lambda s: ImageFont.truetype(F+'LXGWWenKai-Regular.ttf',s)
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
for sfx in era_names.SUFFIXES[1:]:
    for g_ in ('inf','arch','cav','ha'): WHO[g_+era_names.tag(sfx)]=(era_names.ZH[sfx][g_],era_names.EN_LONG[sfx][g_])
GOLD=(206,168,96); W,H=760,200
import json
LACQ={'charge':(120,20,12),'hold':(22,20,19),'retreat':(20,30,70)}   # 和令牌漆色一致（GameSetup 里的 lacq）
for g,(icon,gz,ge) in GEST.items():
    for w,(wz,we) in WHO.items():
        for lang in ('zh','en'):
            if lang=='zh': a,b,fa,fb=wz,gz,zh(48),zh(72)
            else: a,b,fa,fb=we,ge,en(34 if len(we)<10 else 26),en(56)
            md=ImageDraw.Draw(Image.new('RGBA',(8,8)))
            W=int(200+md.textlength(a,font=fa)+18+10+18+md.textlength(b,font=fb)+64)
            im=Image.new('RGBA',(W,H),(0,0,0,0))
            sh=Image.new('RGBA',(W,H),(0,0,0,0)); ImageDraw.Draw(sh).rounded_rectangle([14,18,W-8,H-6],radius=34,fill=(0,0,0,120))
            im.alpha_composite(sh.filter(ImageFilter.GaussianBlur(7)))
            d=ImageDraw.Draw(im)
            d.rounded_rectangle([8,8,W-14,H-14],radius=30,fill=(17,14,12,236),outline=GOLD+(255,),width=3)
            d.rounded_rectangle([18,18,W-24,H-24],radius=22,outline=GOLD+(70,),width=1)
            # 金圈 + 手势
            cx,cy,r=104,(H-6)//2,70
            d.ellipse([cx-r,cy-r,cx+r,cy+r],fill=(28,22,18,255),outline=GOLD+(255,),width=3)
            d.ellipse([cx-r+5,cy-r+5,cx+r-5,cy+r-5],fill=LACQ[g]+(255,))
            # 令徽：和令牌上刻的同一张书法剪影（ArtSource/Tokens/emblem_<令>.png，11-12），等比缩进圈里 70% 的方框
            em=Image.open(R+f'ArtSource/Tokens/emblem_{g}.png').convert('L'); em=Image.eval(em,lambda v:255-v)
            k=(2*r*0.7)/max(em.size); em=em.resize((max(1,int(em.width*k)),max(1,int(em.height*k))),Image.LANCZOS)
            m=Image.new('L',(2*r,2*r),0); m.paste(em,(r-em.width//2,r-em.height//2))
            gl=Image.new('RGBA',(2*r,2*r),(236,190,104,255)); gl.putalpha(m)
            im.alpha_composite(gl,(cx-r,cy-r)); d=ImageDraw.Draw(im)
            # 字：谁（小、骨白）· 做什么（大、金）
            x=200
            d.text((x,cy),a,font=fa,fill=(226,216,196,255),anchor='lm'); x+=d.textlength(a,font=fa)+18
            d.ellipse([x,cy-5,x+10,cy+5],fill=GOLD+(255,)); x+=10+18
            d.text((x,cy+2),b,font=fb,fill=(242,200,124,255),anchor='lm')
            im.save(A+f'order_{g}_{w}_{lang}.png')
W=900; prev=Image.new('RGBA',(W*2,H*3),(120,128,96,255))
for i,(g,w) in enumerate([('charge','all'),('hold','inf'),('retreat','cav')]):
    for j,l in enumerate(('zh','en')):
        t=Image.open(A+f'order_{g}_{w}_{l}.png'); prev.alpha_composite(t,(j*W,i*H))
prev.save('/tmp/sp2/order_prev.png'); print('badges ok')

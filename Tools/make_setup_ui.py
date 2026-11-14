"""开局摆沙盘用的两样东西（11-10 用户：先强制摆好沙盘，把手要好操作）：
  btn_ready_<zh|en>.png  “放好了 / READY” 朱漆圆印（木框前沿正中，手指点一下确认）
  handle_knob.png        把手：墨底金圈圆钮 + 骨白捏合手（比原来的手形图标大、一眼看出能捏）
python3 Tools/make_setup_ui.py → ArtSource/Cover/v6/alpha/"""
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
R='/Users/ld/Documents/ChatGPT/WarTable/'
A=R+'ArtSource/Cover/v6/alpha/'; F=R+'ArtSource/Fonts/'
S=512
def seal(fill, ring, wobble=0.05, seed=1):
    rng=np.random.default_rng(seed)
    yy,xx=np.mgrid[0:S,0:S]; c=S/2; ang=np.arctan2(yy-c,xx-c); dd=np.hypot(xx-c,yy-c)/(S*0.44)
    edge=1+wobble*np.sin(ang*9+1.1)+0.02*np.sin(ang*23)
    a=np.clip((edge-dd)/0.025,0,1)
    im=np.zeros((S,S,4)); im[...,:3]=fill; im[...,3]=a*255
    out=Image.fromarray(im.astype(np.uint8))
    sh=Image.new('RGBA',(S,S),(0,0,0,0)); sh.putalpha(Image.fromarray((a*120).astype(np.uint8)).filter(ImageFilter.GaussianBlur(10)))
    base=Image.new('RGBA',(S,S),(0,0,0,0)); base.alpha_composite(sh,(4,10)); base.alpha_composite(out)
    d=ImageDraw.Draw(base); r=S*0.355; d.ellipse((c-r,c-r,c+r,c+r),outline=ring,width=10)
    return base
zh=ImageFont.truetype(F+'MaShanZheng.ttf',104)
cz=ImageFont.truetype(F+'Cinzel.ttf',64)
try: cz.set_variation_by_axes([800])
except Exception: pass
for lang,txt,font in (('zh','放好了',zh),('en','READY',cz)):
    im=seal((150,30,24),(236,196,112,255))
    d=ImageDraw.Draw(im)
    d.text((S/2,S/2+(6 if lang=='zh' else 4)),txt,font=font,fill=(250,238,214,255),anchor='mm')
    im.save(A+f'btn_ready_{lang}.png')
k=seal((22,18,15),(214,170,92,255),wobble=0.0,seed=2)
hand=Image.open(A+'hand_pinch.png').convert('RGBA').resize((330,330),Image.LANCZOS)
k.alpha_composite(hand,((S-330)//2,(S-330)//2))
k.save(A+'handle_knob.png')
prev=Image.new('RGBA',(S*3+40,S),(120,140,90,255))
for i,n in enumerate(['btn_ready_zh','btn_ready_en','handle_knob']): prev.alpha_composite(Image.open(A+n+'.png'),(i*(S+20),0))
prev.resize((prev.width//2,prev.height//2)).save('/tmp/wt_fix/setup_ui.png'); print('ok')

# 11-10 用户：兵法里说主将，但看不到主将在哪 → 主将头顶的金色“帅”字牌（英文版是金色王冠形的 GEN）
for lang in ('zh','en'):
    b=seal((178,128,48),(250,226,160,255),wobble=0.03,seed=5)
    d=ImageDraw.Draw(b); c=S/2; r=S*0.3; d.ellipse((c-r,c-r,c+r,c+r),fill=(40,24,12,255))
    if lang=='zh': d.text((c,c+6),'帅',font=ImageFont.truetype(F+'MaShanZheng.ttf',190),fill=(250,214,130,255),anchor='mm')
    else:
        f=ImageFont.truetype(F+'Cinzel.ttf',92)
        try: f.set_variation_by_axes([900])
        except Exception: pass
        d.text((c,c+4),'GEN',font=f,fill=(250,214,130,255),anchor='mm')
    b.save(A+f'badge_general_{lang}.png')
print('general ok')

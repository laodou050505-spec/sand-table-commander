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
zh=ImageFont.truetype(F+'SourceHanSerifCN-Heavy.otf',104)
cz=ImageFont.truetype(F+'Cinzel.ttf',64)
try: cz.set_variation_by_axes([800])
except Exception: pass
for lang,txt,font in (('zh','放好了',zh),('en','READY',cz)):
    im=seal((150,30,24),(236,196,112,255))
    d=ImageDraw.Draw(im)
    d.text((S/2,S/2+(6 if lang=='zh' else 4)),txt,font=font,fill=(250,238,214,255),anchor='mm')
    im.save(A+f'btn_ready_{lang}.png')
# 11-17：全套界面换成拼贴纸片 → 把手是一枚藏青圆纸片（细金双圈 + 刻度），中间一只象牙白的捏合手（只取手的线稿，不带原来的墨团底）
import os as _os, sys as _sys; _sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import collage_kit as ck
def hand_only(name, size):
    if _os.path.exists(A+name+'_line.png'): return Image.open(A+name+'_line.png').convert('RGBA').resize((size,size),Image.LANCZOS)   # 铜版画重绘稿
    h=np.asarray(Image.open(A+name+'.png').convert('RGBA'),np.float32); lum=h[...,:3].max(-1)/255
    m=np.clip((lum-0.45)/0.3,0,1)*(h[...,3]/255)                      # 手是骨白的、墨团是黑的：按亮度分开
    out=np.zeros(h.shape,np.float32); out[...,:3]=ck.IVORY; out[...,3]=m*255
    return Image.fromarray(out.astype(np.uint8),'RGBA').resize((size,size),Image.LANCZOS)
k=ck.ring_disc(S,ck.NAVY,seed=2,field=0.6,rays=0.0)      # 边上一圈是真正的铜版画刻度（取自日轮原件）
k.alpha_composite(hand_only('hand_pinch',270),((S-270)//2,(S-270)//2))
k.save(A+'handle_knob.png')
# 暂停键：同一种圆纸片，中间两道象牙白竖杠
pz=ck.ring_disc(S,ck.NAVY,seed=4,field=0.6); d=ImageDraw.Draw(pz)
for x in (S/2-50,S/2+20): d.rounded_rectangle((x,S/2-66,x+30,S/2+66),radius=5,fill=ck.IVORY+(255,))
pz.save(A+'btn_pause.png')
prev=Image.new('RGBA',(S*3+40,S),(120,140,90,255))
for i,n in enumerate(['btn_ready_zh','btn_ready_en','handle_knob']): prev.alpha_composite(Image.open(A+n+'.png'),(i*(S+20),0))
prev.resize((prev.width//2,prev.height//2)).save('/tmp/wt_fix/setup_ui.png'); print('ok')

# 11-10 用户：兵法里说主将，但看不到主将在哪 → 主将头顶的金色“帅”字牌（英文版是金色王冠形的 GEN）
# 11-17：不要中国风 → “帅”字章换成一枚圆纸片上的金王冠（中英文同一张图，不再写字）
def crown(d, cx, cy, w, col):
    h=w*0.62; x0=cx-w/2; yb=cy+h*0.42; yt=cy-h*0.5
    pts=[(x0,yb),(x0-w*0.04,yt+h*0.2),(x0+w*0.2,yt+h*0.52),(x0+w*0.3,yt+h*0.08),(cx-w*0.1,yt+h*0.5),(cx,yt-h*0.06),(cx+w*0.1,yt+h*0.5),(x0+w*0.7,yt+h*0.08),(x0+w*0.8,yt+h*0.52),(x0+w*1.04,yt+h*0.2),(x0+w,yb)]
    d.polygon(pts,fill=col)
    d.rounded_rectangle((x0-2,yb+h*0.07,x0+w+2,yb+h*0.27),radius=4,fill=col)
    for px,py in ((x0-w*0.04,yt+h*0.2),(x0+w*0.3,yt+h*0.08),(cx,yt-h*0.06),(x0+w*0.7,yt+h*0.08),(x0+w*1.04,yt+h*0.2)): d.ellipse((px-w*0.045,py-w*0.045-h*0.06,px+w*0.045,py+w*0.045-h*0.06),fill=col)
for lang in ('zh','en'):
    b=ck.ring_disc(S,(74,52,16),ring=(250,222,150),seed=5,field=0.6)
    b3=Image.new('RGBA',(S*3,S*3),(0,0,0,0)); crown(ImageDraw.Draw(b3),S*1.5,S*1.5+8,S*3*0.3,(250,214,130,255))
    b.alpha_composite(b3.resize((S,S),Image.LANCZOS))
    b.save(A+f'badge_general_{lang}.png')
print('general ok')

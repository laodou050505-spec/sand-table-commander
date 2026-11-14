"""部队状态小牌（11-08 用户：都站着，不知道他们在干嘛）：我军旗牌正上方，漆牌 + 细金边 + 一个词。
颜色：行动中 = 朱红（进攻 / 冲锋 / 交战），行进 = 金，坚守 = 青，待命 / 撤退 = 灰褐。
python3 Tools/make_state_badges.py → ArtSource/Cover/v6/alpha/state_<状态>_<zh|en>.png"""
from PIL import Image, ImageDraw, ImageFont, ImageFilter
R='/Users/ld/Documents/ChatGPT/WarTable/'
A=R+'ArtSource/Cover/v6/alpha/'; F=R+'ArtSource/Fonts/'
def wk(s): return ImageFont.truetype(F+'LXGWWenKai-Regular.ttf',s)
def cz(s):
    f=ImageFont.truetype(F+'Cinzel.ttf',s)
    try: f.set_variation_by_axes([700])
    except Exception: pass
    return f
STATES={'idle':('待命','STANDBY',(70,62,54)),'move':('行进','MARCHING',(150,112,40)),'attack':('进攻','ATTACKING',(150,30,24)),
        'charge':('冲锋','CHARGING',(170,36,26)),'fight':('交战','FIGHTING',(190,44,30)),'hold':('坚守','HOLDING',(36,84,104)),'retreat':('撤退','RETREAT',(90,86,80)),
        # 11-08：布阵时预定的进攻目标——深墨底、金字，和交战的红色区分开（还没打）
        'aim_inf':('目标·步兵','TARGET INFANTRY',(40,34,30)),'aim_arch':('目标·弓兵','TARGET ARCHERS',(40,34,30)),
        'aim_cav':('目标·骑兵','TARGET CAVALRY',(40,34,30)),'aim_ha':('目标·骑射','TARGET H.ARCHERS',(40,34,30))}
# 11-13：目标牌按风格各出一套（state_aim_<兵种>_<后缀>_<语言>；中古那套文件名不变）
import os as _os, sys as _sys; _sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import era_names
for sfx in era_names.SUFFIXES:
    for g in ('inf','arch','cav','ha'):
        en_=era_names.EN_LONG[sfx][g]
        STATES[f'aim_{g}{era_names.tag(sfx)}']=('目标·'+era_names.ZH[sfx][g],'TARGET '+('H.ARCHERS' if en_=='HORSE ARCHERS' else en_),(40,34,30))
H=96
for k,(zh,en,col) in STATES.items():
    for lang,txt,f in (('zh',zh,wk(64)),('en',en,cz(44))):
        d0=ImageDraw.Draw(Image.new('RGBA',(8,8))); tw=d0.textlength(txt,font=f)
        W=int(tw+76); W+=(-W)%4
        im=Image.new('RGBA',(W,H),(0,0,0,0))
        sh=Image.new('RGBA',(W,H),(0,0,0,0)); ImageDraw.Draw(sh).rounded_rectangle([6,10,W-4,H-4],radius=36,fill=(0,0,0,130))
        im.alpha_composite(sh.filter(ImageFilter.GaussianBlur(4)))
        d=ImageDraw.Draw(im)
        d.rounded_rectangle([4,4,W-8,H-12],radius=38,fill=col+(245,),outline=(222,180,100,255),width=4)
        d.text(((W-4)/2,(H-8)/2),txt,font=f,fill=(240,200,120,255) if k.startswith('aim') else (246,236,214,255),anchor='mm')
        im.save(A+f'state_{k}_{lang}.png')
prev=Image.new('RGBA',(3000,260),(90,110,60,255)); x=10
for k in STATES:
    for j,lang in enumerate(('zh','en')):
        t=Image.open(A+f'state_{k}_{lang}.png'); prev.alpha_composite(t,(x,10+j*120))
    x+=Image.open(A+f'state_{k}_en.png').width+8
prev.save('/tmp/wt_fix/state_prev.png'); print('state ok')

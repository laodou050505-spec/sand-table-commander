"""部队状态小牌（11-08 用户：都站着，不知道他们在干嘛）：我军旗牌正上方，漆牌 + 细金边 + 一个词。
颜色：行动中 = 朱红（进攻 / 冲锋 / 交战），行进 = 金，坚守 = 青，待命 / 撤退 = 灰褐。
python3 Tools/make_state_badges.py → ArtSource/Cover/v6/alpha/state_<状态>_<zh|en>.png"""
from PIL import Image, ImageDraw, ImageFont, ImageFilter
R='/Users/ld/Documents/ChatGPT/WarTable/'
A=R+'ArtSource/Cover/v6/alpha/'; F=R+'ArtSource/Fonts/'
def wk(s): return ImageFont.truetype(F+'SourceHanSerifCN-Bold.otf',s)
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
import collage_kit as ck   # 11-17：全套界面换成拼贴纸片（和结算板一套）
for sfx in era_names.SUFFIXES:
    for g in ('inf','arch','cav','ha'):
        en_=era_names.EN_LONG[sfx][g]
        STATES[f'aim_{g}{era_names.tag(sfx)}']=('目标·'+era_names.ZH[sfx][g],'TARGET '+('H.ARCHERS' if en_=='HORSE ARCHERS' else en_),(40,34,30))
H=96
for k,(zh,en,col) in STATES.items():
    for lang,txt,f in (('zh',zh,wk(56)),('en',en,cz(40))):   # 11-17 用户：字不要太大 → 64 / 44 收到 56 / 40
        d0=ImageDraw.Draw(Image.new('RGBA',(8,8))); tw=d0.textlength(txt,font=f)
        W=int(tw+76); W+=(-W)%4
        aim=k.startswith('aim')
        tintc=ck.NAVY if aim else tuple(int(c*0.86) for c in col)      # 真纸条：两头是撕的，右端一片网点
        rgb_,a_,(x0,y0,x1,y1)=ck.real_strip(W,H,tintc,pad=9,flip=sum(map(ord,k))%2==1)
        rgb_=ck.corner_dots(rgb_,7,rgb_.shape[1]*1.02,rgb_.shape[0]*0.9,rgb_.shape[1]*0.42,rgb_.shape[0]*1.1,tuple(int(c*0.4) for c in tintc),0.7)
        im=ck.finish(rgb_,a_,9); d=ImageDraw.Draw(im)
        d.line([(x0+16,y0+7),(x0+(x1-x0)*0.55,y0+7)],fill=ck.GOLD_D+(230,),width=2)      # 上沿一段、下沿一段细金线，错开
        d.line([(x0+(x1-x0)*0.5,y1-7),(x1-16,y1-7)],fill=ck.GOLD_D+(230,),width=2)
        d.text((W/2,H/2-1),txt,font=f,fill=(236,196,120,255) if aim else (246,236,214,255),anchor='mm')
        im.save(A+f'state_{k}_{lang}.png')
prev=Image.new('RGBA',(3000,260),(90,110,60,255)); x=10
for k in STATES:
    for j,lang in enumerate(('zh','en')):
        t=Image.open(A+f'state_{k}_{lang}.png'); prev.alpha_composite(t,(x,10+j*120))
    x+=Image.open(A+f'state_{k}_en.png').width+8
prev.save('/tmp/wt_fix/state_prev.png'); print('state ok')

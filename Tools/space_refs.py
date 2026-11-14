"""深空风格（第五个风格，11-13 用户选定样图 C：Docs/Concepts/space/sc_star_lancers.png，并要求“要有重型机甲的感觉”）兵种的定妆图。
做法同 Tools/modern_refs.py：样图 C + 同阵营已通过的试样作风格参考，banana_pro 图生图出“白底 T 字姿势、不拿武器”的单人定妆图
→ ArtSource/Space/refs/<兵种>.png（之后 WT_ERA=space python3 Tools/gothic_chars.py … 做 3D）。
蓝 = 星枪团（原金狮王国 kd_）：钢蓝 + 白搪瓷的增压硬壳重型外骨骼甲，橙色警示条，圆舷窗面罩发青光。
红 = 余烬矿帮（原赤牙氏族 cl_）：焦红 + 炭黑的重型采矿外骨骼，头盔上一簇琥珀色矿灯。
两边都：人穿着的重型机甲（粗壮的装甲四肢、看得见的活塞和液压关节），但仍是人形比例、能绑人形骨骼；不要背包、不要披风、不要悬空 / 垂挂的东西；
原创设计，不照搬任何现成作品；没有文字和标志。
用法：HTTPS_PROXY=http://127.0.0.1:7897 python3 Tools/space_refs.py [兵种...]（已有的跳过；--force 重出；--list 只打印提示词长度；--try 出到 refs/try/）"""
import pathlib, sys, time, urllib.request

sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api

PROJ = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable')
REFS = PROJ / 'ArtSource/Space/refs'
CONCEPT = PROJ / 'Docs/Concepts/space/sc_star_lancers.png'
STYLE = {'blue': REFS / 'try/kd_footman.png', 'red': REFS / 'try/cl_warrior.png'}

# banana_pro 的提示词上限 1800 字符：下面几段都压到最短
MECHA = ('HEAVY MECHA look: a soldier wearing a heavy powered exo-armour frame like a small walking mech, thick armoured limbs, chunky layered plates, '
         'pistons and hydraulic joints at elbows and knees, blocky boots, armoured gauntlets.')
BASE = {
    'blue': 'a Star Lancer trooper: pressurised hard-shell exo-armour in steel blue and white enamel, orange hazard stripes, a round porthole visor glowing pale cyan, ribbed joints',
    'red': 'an Ember Clan asteroid-miner rebel: scorched red and charcoal-black heavy mining exo-suit, dented and sooty, small round amber lamps on the helmet',
}
SHAPE = ('Boxy rectangular shoulder blocks, NOT big round shoulder pads. About seven heads tall with LONG legs (half the total height); the helmet sits on a neck ring '
         'clearly above the shoulders; arms and legs clearly separated from the torso.')
POSE = ('Whole figure from helmet to boot soles fully visible and sharp on an empty pure white backdrop: no diorama, no terrain, no other figures, no fog, no base, no shadow. '
        'Strict T-pose: both arms straight out horizontally at shoulder height (90 degrees, not angled down), palms down, empty open hands, legs straight and slightly apart, '
        'facing straight forward, front view. No weapon, no shield, no text, no numbers, no logos, no skull or eagle emblems, no backpack, no cape, no cloth, no dangling hoses, no antennas. '
        'Original design. Detailed hand-painted 28mm miniature with washes and edge highlights, soft even studio light.')

TROOPS = {
    # ---- 星枪团（kd_）
    'kd_footman': ('blue', 'a line trooper: standard hard-shell armour, a rounded dome helmet with one round porthole visor, one orange hazard stripe across the white chest plate, blocky white knee plates'),
    'kd_guard': ('blue', 'a squad captain: heavier armour with extra layered rectangular shoulder blocks and thigh plates, a dome helmet with a short white fin crest on top, two orange stripes on each upper arm'),
    'kd_spearman': ('blue', 'a light scout frame: slimmer armour with fewer plates and exposed dark joint mesh, a small helmet with a flat horizontal visor band, an orange stripe on one shin'),
    'kd_halberdier': ('blue', 'a tall cordon trooper: long white shin guards and long forearm guards, a helmet with a wide protective collar ring around the neck, an orange stripe down each thigh'),
    'kd_swordsman': ('blue', 'an assault duelist: sleeker close-fitting armour, a helmet with a narrow vertical visor slit, one all-white shoulder plate and one blue shoulder plate, armoured fingers'),
    'kd_hammer': ('blue', 'a heavy breacher: the bulkiest suit, a barrel-shaped chest with a second round porthole in the chest plate, a small low dome head, enormous forearms and fists, very thick legs'),
    'kd_archer': ('blue', 'a marksman: lighter armour, a helmet with one large round targeting lens on the left side and a small porthole, slim forearms, a white chest plate with a thin orange chevron'),
    'kd_crossbow': ('blue', 'a short stocky heavy gunner: a wide body, a flat-topped helmet with boxy sensor blocks at the ears, thick braced forearms, heavy stabiliser boots'),
    'kd_knight': ('blue', 'a lancer rider: streamlined armour with a tall armoured collar, a helmet with a white stripe over the top and a round visor, slim armoured legs suited for riding, nothing hanging below the waist'),
    # ---- 余烬矿帮（cl_）
    'cl_warrior': ('red', 'a miner fighter: a red heavy work exo-suit, a helmet with three amber lamps in a row above a grille face plate, one black-and-yellow hazard-striped shoulder plate, big work gauntlets'),
    'cl_guardian': ('red', 'a foreman warlord: thick charcoal armour plates welded over the red suit, a helmet crowned with a ring of five amber lamps, huge square shoulder blocks, a short armoured skirt of plates (arms must stay perfectly horizontal)'),
    'cl_skirmisher': ('red', 'a light raider: a stripped-down suit with bare frame struts on the arms and legs, a small helmet with a single amber lamp, lean build'),
    'cl_pikeman': ('red', 'a tall lean fighter: a full-face breathing mask with two round filter cans, long piston legs, a narrow chest plate, one amber lamp on each shoulder'),
    'cl_berserker': ('red', 'a wild brawler: a sleeveless heavy frame with huge exposed mechanical exo-arms and oversized piston fists, a cage-bar face mask with two amber lamps, a narrow harness chest'),
    'cl_crusher': ('red', 'a huge brute: a massive power-loader frame covered in welded scrap plates, a drum-shaped helmet with a grille and four amber lamps, very thick legs and enormous square shoulder blocks (both arms MUST be raised straight out sideways, perfectly horizontal, in a T shape)'),
    'cl_hunter': ('red', 'a slender scout: a smooth cowl-shaped helmet with a single large amber eye lens, a light red suit with thin charcoal plates, long forearm braces (arms must stay perfectly horizontal)'),
    'cl_wolfrider': ('red', 'a crawler rider: a helmet with a wide amber visor and two lamps, a charcoal chest protector, knee guards, armoured legs suited for riding, nothing hanging below the waist'),
    'cl_wolfarcher': ('red', 'a light crawler scout: an open-frame helmet with an amber visor, a light red suit, slim build, legs suited for riding'),
}


def query(tid):
    """查任务：v3 域名（openapi.tripo3d.ai）经常连不上，先走 v2 域名（同一个任务号能查），不行再试 v3。"""
    import json
    try:
        req = urllib.request.Request(f'https://api.tripo3d.ai/v2/openapi/task/{tid}', headers={'Authorization': f'Bearer {api.api_key()}', 'User-Agent': 'space-refs/1.0 (+node-fetch compatible)'})
        with urllib.request.urlopen(req, timeout=40) as r: return json.loads(r.read())['data']
    except Exception:
        return api.task(tid)


def prompt(side, desc, ref=False):
    lead = 'ONE single figure, same painted style and faction as the reference figure: ' if ref else 'ONE single figure: '
    return f'{lead}{BASE[side]}. This one is {desc}. {MECHA} {SHAPE} {POSE}'


def submit(name, trying):
    """试样（--try）：纯文字出图，不带样图——带着整张沙盘样图去图生图，会把样图的地面和后排棋子糊成一条雾带盖住腿（11-13 第一次试样）。
    正式：只用同阵营通过的试样当风格参考。"""
    side, desc = TROOPS[name]
    if trying or not STYLE[side].exists():
        body = {'model': 'banana_pro', 'prompt': prompt(side, desc), 'aspect_ratio': '1:1', 'output_format': 'png'}
        return api.request_json('POST', '/generation/text-to-image', body)['data']['task_id']
    body = {'model': 'banana_pro', 'inputs': [api.upload_file(STYLE[side])], 'prompt': prompt(side, desc, True), 'aspect_ratio': '1:1', 'output_format': 'png'}
    return api.request_json('POST', '/generation/image-to-image', body)['data']['task_id']


def main():
    if '--list' in sys.argv:
        for n, (side, desc) in TROOPS.items(): print(n, len(prompt(side, desc, True)))
        return
    force = '--force' in sys.argv; trying = '--try' in sys.argv
    out = REFS / 'try' if trying else REFS
    todo = [a for a in sys.argv[1:] if not a.startswith('--')] or list(TROOPS)
    todo = [n for n in todo if force or not (out / f'{n}.png').exists()]
    out.mkdir(parents=True, exist_ok=True)
    running = {}
    while todo or running:
        while todo and len(running) < 1:   # Tripo 图生图并发很低：一次一张
            n = todo[0]
            try:
                running[n] = submit(n, trying); todo.pop(0); print(time.strftime('%H:%M:%S'), n, 'submitted', flush=True)
            except Exception as e:
                if '429' in str(e) or 'limit' in str(e).lower(): break
                # 断线（Tripo 主域名经代理间歇性连不上：EOF / 超时 / reach）不算失败，等一会儿重试；别的错误才跳过这个兵种
                if any(k in str(e) for k in ('EOF', 'reach', 'timed out', 'urlopen', 'Connection', 'SSL')):
                    print(time.strftime('%H:%M:%S'), n, '断线，稍后重试', flush=True); time.sleep(20); break
                print(n, 'ERROR', str(e)[:200], flush=True); todo.pop(0)
        time.sleep(8)
        for n, tid in list(running.items()):
            try: d = query(tid)
            except Exception as e: print(n, 'query', str(e)[:80], flush=True); continue
            d = d.get('data', d); s = d.get('status')
            if s == 'success':
                o = d['output']; u = o.get('generated_image_url') or o.get('generated_image') or o.get('image_url') or o.get('image')
                req = urllib.request.Request(u, headers={'User-Agent': 'space-refs/1.0'})
                (out / f'{n}.png').write_bytes(urllib.request.urlopen(req, timeout=120).read()); del running[n]
                print(time.strftime('%H:%M:%S'), n, 'ok', flush=True)
            elif s in ('failed', 'cancelled', 'banned', 'expired'):
                print(n, 'FAILED', s, d.get('error_msg'), flush=True); del running[n]


if __name__ == '__main__':
    main()

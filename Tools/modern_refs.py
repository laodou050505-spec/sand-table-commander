"""现代时代兵种的定妆图（11-13 用户选定样图 A：Docs/Concepts/modern/ma_blueshield_vs_redsand.png）。
做法同 Tools/future_refs.py：样图 A + 同阵营已通过的试样作风格参考，banana_pro 图生图出“白底 T 字姿势、不拿武器”的单人定妆图
→ ArtSource/Modern/refs/<兵种>.png（之后 WT_ERA=modern python3 Tools/gothic_chars.py … 做 3D）。
蓝 = 蓝盾特勤（原金狮王国 kd_）：藏青作战服 + 浅灰护甲 / 头盔 + 荧光黄绿识别条。
沙 = 赤砂佣兵（原赤牙氏族 cl_）：沙漠迷彩作战服 + 深棕战术背心 + 红围巾 / 红臂章，墨镜或蒙面巾。
两边都：现实题材、布料和真实装备，不是科幻装甲；不要背包、不要披风、不要悬空 / 垂挂的东西；没有真实国家或组织的标志、没有文字；不血腥。
用法：HTTPS_PROXY=http://127.0.0.1:7897 python3 Tools/modern_refs.py [兵种...]（已有的跳过；--force 重出；--list 只打印提示词长度；--try 出到 refs/try/）"""
import pathlib, sys, time, urllib.request

sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api

PROJ = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable')
REFS = PROJ / 'ArtSource/Modern/refs'
CONCEPT = PROJ / 'Docs/Concepts/modern/ma_blueshield_vs_redsand.png'
STYLE = {'blue': REFS / 'try/kd_footman.png', 'sand': REFS / 'try/cl_warrior.png'}

BASE = {
    'blue': ('the Blue Shield tactical officers in the NEAR row of the reference image (same painted miniature look: navy-blue cloth tactical uniform, light grey '
             'protective vest and pads, bright yellow-green hi-vis stripes, black boots and gloves)'),
    'sand': ('the Red Sand mercenary fighters in the FAR row of the reference image (same painted miniature look: desert-tan camouflage cloth fatigues, dark brown '
             'load-bearing vest, red neck scarf and red armband, tan boots)'),
}
POSE = ('Pose: strict T-pose, both arms straight out horizontally at shoulder height (exactly 90 degrees from the body, NOT angled down, NOT an A-pose), palms down, empty open hands, legs straight and slightly apart, head facing '
        'straight forward, full body head to feet, front view. Pure plain white background, no base, no ground, no shadow, no smoke, no haze, no weapon, no shield, no holster gun, nothing '
        'in the hands, no text, no letters, no flags, no real-world insignia. Everything is compact and attached to the body: no backpack, no cape, no dangling straps, '
        'no antennas. Realistic modern-day clothing and gear, NOT science fiction. Realistic human proportions. Highly detailed hand-painted miniature figurine with washes and edge highlights, soft even studio lighting.')

TROOPS = {
    # ---- 蓝盾特勤（kd_）
    'kd_footman': ('blue', 'a riot-line officer: a navy riot helmet with a clear face visor, a light grey chest protector with one yellow-green stripe across it, grey elbow and knee pads'),
    'kd_guard': ('blue', 'a squad captain: heavier light-grey riot armor with shoulder guards and thigh guards, a navy helmet with a yellow-green stripe over the top and the visor raised, two yellow-green stripes on each upper arm'),
    'kd_spearman': ('blue', 'a light patrol trooper: a navy baseball cap, a slim light-grey vest over a long-sleeved navy shirt, a yellow-green armband, no helmet, no pads'),
    'kd_halberdier': ('blue', 'a cordon trooper: a navy helmet with a neck guard and clear visor, long light-grey forearm guards and shin guards, a yellow-green stripe down each trouser leg'),
    'kd_swordsman': ('blue', 'a special-response operator: a navy balaclava with clear goggles, a slim close-fitting dark navy plate vest with one yellow-green chest stripe, fingerless gloves'),
    'kd_hammer': ('blue', 'a heavy breacher: a bulky padded light-grey blast suit with a high protective collar, a big rounded navy helmet with a clear visor, thick boots'),
    'kd_archer': ('blue', 'a marksman: a navy boonie hat, shooting glasses, a light navy field shirt with rolled cuffs, a slim light-grey chest rig with a yellow-green shoulder patch'),
    'kd_crossbow': ('blue', 'a short stocky heavy gunner: a wide light-grey armored vest, a navy helmet with ear protectors and a yellow-green band, thick forearms, heavy boots'),
    'kd_knight': ('blue', 'a motorcycle patrol officer: a white open-face motorcycle helmet with a clear visor and a blue stripe, a navy riding jacket with a yellow-green diagonal sash stripe, tall black riding boots, nothing hanging below the waist'),
    # ---- 赤砂佣兵（cl_）
    'cl_warrior': ('sand', 'a fighter: a tan field cap, dark sunglasses, a red scarf around the neck, sleeves rolled to the elbow, tan gloves'),
    'cl_guardian': ('sand', 'a warlord: a red beret, a thick dark-brown armored plate vest with extra shoulder pads, a short beard, a red sash belt around the waist, forearms only slightly thicker than normal (arms must stay perfectly horizontal)'),
    'cl_skirmisher': ('sand', 'a light raider: a red bandana headband, a tan t-shirt, a slim dark-brown chest rig, lean build, fingerless gloves'),
    'cl_pikeman': ('sand', 'a tall lean fighter: a red cloth face wrap covering nose and mouth, dust goggles pushed up on the forehead, a tan field jacket, long legs'),
    'cl_berserker': ('sand', 'a wild brawler: a shaved head, a red cloth mask over the lower face, a sleeveless tan shirt showing muscular tattooed arms, wrapped fists, only a narrow brown harness'),
    'cl_crusher': ('sand', 'a huge brute: improvised scrap armor made of rusty steel plates and black tire rubber strapped over tan fatigues, a rusty welding mask covering the face, very thick legs'),
    'cl_hunter': ('sand', 'a slender scout: a short tan hooded scarf covering head and lower face with only the eyes visible, light tan clothes, long forearm wraps (arms must stay perfectly horizontal)'),
    'cl_wolfrider': ('sand', 'a dirt-bike raider: a tan motocross helmet with a red stripe and dust goggles, a dark-brown chest protector, knee guards, legs suited for riding, nothing hanging below the waist'),
    'cl_wolfarcher': ('sand', 'a light dirt-bike scout: an open-face tan helmet with goggles, a red scarf over the mouth, a light tan jacket, slim build, legs suited for riding'),
}


def query(tid):
    """查任务：v3 域名（openapi.tripo3d.ai）经常连不上，先走 v2 域名（同一个任务号能查），不行再试 v3。"""
    import json
    try:
        req = urllib.request.Request(f'https://api.tripo3d.ai/v2/openapi/task/{tid}', headers={'Authorization': f'Bearer {api.api_key()}', 'User-Agent': 'wartable-modern/1.0 (+node-fetch compatible)'})
        with urllib.request.urlopen(req, timeout=40) as r: return json.loads(r.read())['data']
    except Exception:
        return api.task(tid)


def prompt(side, desc):
    return f'ONE single figure in the style of {BASE[side]}. The figure: a modern-day {desc}. {POSE}'


def submit(name, trying):
    side, desc = TROOPS[name]
    refs = [CONCEPT] + ([STYLE[side]] if STYLE[side].exists() and not trying else [])
    body = {'model': 'banana_pro', 'inputs': [api.upload_file(p) for p in refs], 'prompt': prompt(side, desc), 'aspect_ratio': '1:1', 'output_format': 'png'}
    return api.request_json('POST', '/generation/image-to-image', body)['data']['task_id']


def main():
    if '--list' in sys.argv:
        for n, (side, desc) in TROOPS.items(): print(n, len(prompt(side, desc)))
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
                req = urllib.request.Request(u, headers={'User-Agent': 'wartable-modern/1.0'})
                (out / f'{n}.png').write_bytes(urllib.request.urlopen(req, timeout=120).read()); del running[n]
                print(time.strftime('%H:%M:%S'), n, 'ok', flush=True)
            elif s in ('failed', 'cancelled', 'banned', 'expired'):
                print(n, 'FAILED', s, d.get('error_msg'), flush=True); del running[n]


if __name__ == '__main__':
    main()

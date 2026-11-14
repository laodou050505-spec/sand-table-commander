"""未来时代兵种的定妆图（11-12 用户选定样图 A：Docs/Concepts/future/fa_prism_vs_void.png；B / C 留给以后别的风格）。
做法同 Tools/gothic_refs.py：样图 A + 同阵营已通过的试样作风格参考，banana_pro 图生图出“白底 T 字姿势、不拿武器”的单人定妆图
→ ArtSource/Future/refs/<兵种>.png（之后照 gothic_chars.py 的流程做 3D）。
白 = 光棱军团（原金狮王国 kd_）：珍珠白陶瓷甲片 + 深灰内衬 + 细金边，无面罩头盔上一道天青色光缝、头顶小鳍；武器是天青色硬光。
黑 = 虚空蜂群（原赤牙氏族 cl_）：哑光黑曜石色甲虫壳式机械外骨骼 + 紫色缝光，两只斜的紫色眼镜片、短触角；干净硬表面，不黏、不是血肉、没有孔洞。
两边都：不要披风、不要背包、不要悬空部件、不要大圆肩甲，写实人体比例，不像任何现成作品。
用法：HTTPS_PROXY=http://127.0.0.1:7897 python3 Tools/future_refs.py [兵种...]（已有的跳过；--force 重出；--list 只打印提示词长度）"""
import pathlib, sys, time, urllib.request

sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api

PROJ = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable')
REFS = PROJ / 'ArtSource/Future/refs'
CONCEPT = PROJ / 'Docs/Concepts/future/fa_prism_vs_void.png'
STYLE = {'white': REFS / 'try/kd_footman.png', 'black': REFS / 'try/cl_warrior.png'}

BASE = {
    'white': ('the white armored Prism Legion soldier in the reference images (same painted miniature look: smooth pearl-white ceramic armor plates over a dark '
              'charcoal-grey undersuit, thin gold trim lines, faceless smooth helmet with ONE horizontal glowing sky-cyan visor line, small cyan lights)'),
    'black': ('the black armored Void Swarm warrior in the reference images (same painted miniature look: matte obsidian-black segmented beetle-shell mechanical '
              'exoskeleton, thin glowing violet seams, angular insect-like helmet with two slanted glowing violet eye lenses; clean hard-surface machine armor, '
              'NOT slimy, NOT flesh, no holes)'),
}
POSE = ('Pose: strict T-pose, both arms straight out horizontally at shoulder height (exactly 90 degrees from the body, NOT angled down, NOT an A-pose), palms down, empty open hands, legs straight and slightly apart, head facing '
        'straight forward, full body head to feet, front view. Pure plain white background, no base, no ground, no shadow, no smoke, no weapon, no shield, nothing '
        'in the hands, no text. Everything attached to the body is solid and compact: no capes, no backpack, no floating parts, no thin dangling strips, no big '
        'round shoulder pads. Realistic human proportions. Original design, not from any existing franchise. Highly detailed painted miniature figurine, soft even studio lighting.')

TROOPS = {
    # ---- 光棱军团（kd_）
    'kd_footman': ('white', 'a line infantryman: layered petal-like chest and thigh plates, a small swept-back fin crest on the helmet, a small cyan light on the chest'),
    'kd_guard': ('white', 'an elite captain: heavier layered plates with richer gold trim, a gold collar ring, a taller single fin crest, short gold-edged hip plates, a larger cyan chest light'),
    'kd_spearman': ('white', 'a light trooper: only a slim white chest plate, forearm guards and greaves over the dark undersuit, a small round helmet with a narrow cyan visor band and no crest'),
    'kd_halberdier': ('white', 'a pole-arm trooper: segmented white armor with reinforced long forearm guards, a helmet with two small side fins, a cyan stripe down each thigh plate'),
    'kd_swordsman': ('white', 'a duelist: sleek close-fitting white armor with an angular gold-edged ridge down the chest, a helmet with one long back-swept blade-like crest'),
    'kd_hammer': ('white', 'a heavy trooper: thick blocky white plates with a reinforced high collar, a low wide helmet with a T-shaped cyan visor, broad armored boots, a square cyan power cell on the chest'),
    'kd_archer': ('white', 'a ranger: light white armor, a short dark-grey hood shape fused to the helmet, a single round cyan monocle lens over one eye instead of a visor line, slim bracers'),
    'kd_crossbow': ('white', 'a stocky short heavy gunner: wide barrel-chested white armor, a round dome helmet with a cyan goggle band, thick forearm guards, heavy boots'),
    'kd_knight': ('white', 'a mounted lancer: streamlined white armor with gold trim, a tall fin crest, smooth armored legs suited for riding, nothing hanging below the waist'),
    # ---- 虚空蜂群（cl_）
    'cl_warrior': ('black', 'a warrior: two short swept-back antennae fused to the helmet, a violet light core on the chest, clawed gauntlets, digit-toed armored boots'),
    'cl_guardian': ('black', 'a hive chief: heavier thick layered shell plates on chest and shoulders, a crown of three short swept-back horns fused to the helmet, a larger violet chest core, forearm plates only slightly thicker than normal (arms must stay perfectly horizontal)'),
    'cl_skirmisher': ('black', 'a light skirmisher: slim build, small shell plates only on the chest, forearms and shins over a dark flexible ribbed undersuit, a narrow smooth helmet'),
    'cl_pikeman': ('black', 'a tall lean stalker: elongated forearm plates, a long backward-swept mantis-like head crest, narrow waist, long shin plates'),
    'cl_berserker': ('black', 'a berserker: broad bare-looking smooth chest plate covered with glowing violet circuit lines, thin arm armor, oversized clawed forearm shells, a short jagged-edged helmet'),
    'cl_crusher': ('black', 'a huge brute: massive rhinoceros-beetle shell armor, one thick short horn on the front of the helmet, a big violet core in the belly plate, very thick legs'),
    'cl_hunter': ('black', 'a slender hunter: a smooth teardrop-shaped helmet shell with two bright violet eye lenses and no antennae, light thin plates, long forearm guards (arms must stay perfectly horizontal)'),
    'cl_wolfrider': ('black', 'a beast rider: medium streamlined shell armor, two SHORT THICK horn-like antennae swept back flat against the helmet (no long thin antennae), armored legs suited for riding, nothing hanging below the waist'),
    'cl_wolfarcher': ('black', 'a light beast-rider archer: slim armor, a single glowing violet visor band instead of two lenses, light bracers, legs suited for riding'),
}


def prompt(side, desc):
    return f'ONE single figure in the style of {BASE[side]}. The figure: a far-future {desc}. {POSE}'


def submit(name):
    side, desc = TROOPS[name]
    refs = [CONCEPT] + ([STYLE[side]] if STYLE[side].exists() else [])
    body = {'model': 'banana_pro', 'inputs': [api.upload_file(p) for p in refs], 'prompt': prompt(side, desc), 'aspect_ratio': '1:1', 'output_format': 'png'}
    return api.request_json('POST', '/generation/image-to-image', body)['data']['task_id']


def main():
    if '--list' in sys.argv:
        for n, (side, desc) in TROOPS.items(): print(n, len(prompt(side, desc)))
        return
    force = '--force' in sys.argv
    todo = [a for a in sys.argv[1:] if not a.startswith('--')] or list(TROOPS)
    todo = [n for n in todo if force or not (REFS / f'{n}.png').exists()]
    REFS.mkdir(parents=True, exist_ok=True)
    running = {}
    while todo or running:
        while todo and len(running) < 1:   # Tripo 图生图并发很低：一次一张
            n = todo[0]
            try:
                running[n] = submit(n); todo.pop(0); print(time.strftime('%H:%M:%S'), n, 'submitted', flush=True)
            except Exception as e:
                if '429' in str(e) or 'limit' in str(e).lower(): break
                print(n, 'ERROR', str(e)[:200], flush=True); todo.pop(0)
        time.sleep(8)
        for n, tid in list(running.items()):
            try: d = api.task(tid)
            except Exception as e: print(n, 'query', str(e)[:80], flush=True); continue
            d = d.get('data', d); s = d.get('status')
            if s == 'success':
                o = d['output']; u = o.get('generated_image_url') or o.get('generated_image') or o.get('image_url')
                req = urllib.request.Request(u, headers={'User-Agent': 'wartable-future/1.0'})
                (REFS / f'{n}.png').write_bytes(urllib.request.urlopen(req, timeout=120).read()); del running[n]
                print(time.strftime('%H:%M:%S'), n, 'ok', flush=True)
            elif s in ('failed', 'cancelled', 'banned', 'expired'):
                print(n, 'FAILED', s, d.get('error_msg'), flush=True); del running[n]


if __name__ == '__main__':
    main()

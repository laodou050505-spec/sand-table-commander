"""哥特科幻时代兵种的定妆图（11-10 用户选定 s1c；kd_footman / cl_warrior 两张试图通过）。
每个兵种：s1c 概念图 + 同阵营已通过的试图作风格参考，banana_pro 图生图出“白底 T 字姿势、不拿武器”的单人定妆图
→ ArtSource/Gothic/refs/<兵种>.png（之后 Tools/gothic_chars.py 拿它做 3D）。
蓝 = 圣钟骑士团（深蓝搪瓷钢 + 铜饰、兜帽哥特盔、铜钟 / 香炉）；不用 40k 的大圆肩甲、纯洁封条。
红 = 炉铁氏族（锈铁铆钉锅炉板、胸口发光炉门、焊工面罩、皮围裙、灰皮肤）；无角、无刺、无骷髅、不是绿皮。
用法：python3 Tools/gothic_refs.py [兵种...]（已有的跳过；--force 重出）"""
import json, pathlib, sys, time, urllib.request

sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api

PROJ = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable')
REFS = PROJ / 'ArtSource/Gothic/refs'
S1C = PROJ / 'Docs/Concepts/s1c_gothic_steam.png'
STYLE_BLUE = REFS / 'try_footman/kd_footman.png'
STYLE_RED = REFS / 'try_warrior/cl_warrior.png'

BLUE = ('the navy-blue gothic armored figures of the Bell Order in the reference images (same painted miniature look, navy blue enamelled steel with bronze '
        'filigree trim, hooded gothic helmets, small bronze bells)')
RED = ('the rust-iron Forge Clan warrior in the reference images (same painted miniature look: rusty riveted iron boiler plates, a small glowing orange furnace '
       'grille on the chest, scorched leather, soot-darkened grey skin, welding-mask style helmets with round amber glass lenses). NO horns, NO spikes, NO skulls, NOT green skin')
POSE = ('Pose: standing in a strict T-pose, both arms held straight out horizontally to the sides at shoulder height (not angled down), palms down, empty open hands, '
        'legs straight and slightly apart, full body from head to feet, facing the camera, front view. Pure plain white background, no base, no pedestal, no ground, '
        'no grass, no shadow, no smoke, no steam, no fire particles, no weapon, no shield, no bow, nothing in the hands, no text. '
        'Everything attached to the body must be solid and compact (no loose chains, no floating parts, no long thin dangling strips). '
        'Highly detailed painted miniature figurine, soft even studio lighting.')

TROOPS = {
    # ---- 圣钟骑士团（原 金狮王国 kd_）
    'kd_guard': ('blue', 'an elite bell-warden captain in heavy fluted navy blue plate armor with rich bronze filigree, a tall gothic helmet under a pointed blue hood with a glowing pale-blue visor slit, a deep blue tabard, a compact bronze bell on the back between the shoulder blades'),
    'kd_spearman': ('blue', 'a lightly armored militia spearman in a quilted dark blue coat with steel bracers and a bronze-trimmed steel gorget, a simple round steel cap with a short blue hood, a small bronze lantern clipped to the belt'),
    # 11-10 自查：第一版香炉用链子吊在身侧（悬空挂件 → P2 浮块、动作穿模）→ 不要香炉
    'kd_halberdier': ('blue', 'a halberdier in segmented navy blue armor plates over a dark mail shirt, a hooded close helmet with a narrow slit, a bronze bell emblem engraved on the breastplate, nothing hanging from the body'),
    'kd_swordsman': ('blue', 'a greatsword zealot in heavy navy blue gothic plate armor with a bronze-trimmed arched helmet with a vertical slit visor, a short blue cloth skirt over armored legs, a bronze bell emblem on the chest'),
    # 11-10 自查：第一版大圆肩甲 + 背包太像 40k 终结者 → 改成哥特全身板甲（尖拱、凹槽纹），普通大小的肩甲、没有背包
    'kd_hammer': ('blue', 'a heavy templar in full fluted gothic plate armor in navy blue enamel with bronze edges, pointed gothic arches and ridges on the plates, normal-sized layered pauldrons that follow the shoulder shape (NOT big round shoulder pads, NOT power armor, no backpack), a tall hooded bascinet helmet with a narrow slit, a short chainmail skirt, realistic human proportions'),
    'kd_archer': ('blue', 'a ranger in a hooded dark blue cloak over a light navy blue steel cuirass, a single brass monocle lens on the hood, leather bracers, a leather quiver strap across the chest (no quiver arrows sticking out)'),
    'kd_crossbow': ('blue', 'a stocky dwarf arbalester with a short braided grey beard, a round navy blue enamelled helmet with brass goggles pushed up, a dark blue padded coat over plate, a leather bandolier with brass cartridges, heavy armored boots'),
    # 11-10 自查：第一版披风拖地（骑马时插进马身）→ 不要披风
    'kd_knight': ('blue', 'a mounted knight commander in ornate navy blue gothic plate armor with bronze trim, a tall arched gothic helmet with a short blue crest, NO cape, NO cloak, nothing hanging below the waist except short armored tassets, armored legs suited for riding'),
    # ---- 炉铁氏族（原 赤牙氏族 cl_）
    'cl_warrior': ('red', 'a hulking forge-clan raider with muscular grey arms, rusty riveted iron boiler-plate shoulder guards, a riveted iron chest plate with a small glowing orange furnace grille, a heavy scorched leather apron over dark trousers, iron bracers, a round blacksmith welding-mask style iron helmet with two round amber lenses, a very short iron chimney stub on the back of one shoulder'),
    'cl_guardian': ('red', 'a forge-clan chief in heavy rust-red riveted iron plate armor with brass rivets, a larger glowing orange furnace door in the chest, a tall riveted iron helmet with a welding visor slit and two amber lenses, thick iron gauntlets, a chain belt with a big iron buckle'),
    'cl_pikeman': ('red', 'a forge-clan tracker in a long soot-stained leather coat reinforced with small iron plates, a leather hood with round brass goggles, leather forearm wraps, grey skin'),
    'cl_berserker': ('red', 'a forge-clan stoker berserker with a bare soot-covered grey muscular torso with glowing orange ember tattoo lines, a heavy scorched leather apron, an iron collar, thick leather gloves, a riveted iron half-helmet with one round amber lens'),
    'cl_crusher': ('red', 'a huge forge brute in massive rusty iron boiler-plate armor with a glowing orange furnace belly door, two very short exhaust stubs on the shoulders, a riveted iron bucket helmet with a glowing grille, thick iron boots, broad but realistic humanoid proportions'),
    'cl_skirmisher': ('red', 'a light forge-clan skirmisher in leather wraps and a short leather vest with a few iron rivets, soot war paint on grey skin, a cloth face mask and round brass goggles, leather sandals'),
    'cl_hunter': ('red', 'a forge-clan hunter in a hooded soot-grey leather cloak and a leather vest, glowing amber goggles under the hood, bone-white leather bracers, leather leggings'),
    'cl_wolfrider': ('red', 'a forge-clan beast rider in dark riveted leather armor with small rusty iron plates, a round welding-mask helmet with amber lenses, a short soot-grey fur mantle, armored legs suited for riding'),
    'cl_wolfarcher': ('red', 'a light forge-clan beast-rider archer in light leather armor with a rusty iron collar, a red cloth bandana and round brass goggles, leather bracers, legs suited for riding'),
}


def prompt(side, desc):
    base = BLUE if side == 'blue' else RED
    return (f'Character design reference sheet of ONE single figure, in the style of {base}. The figure: {desc}. '
            f'Realistic proportions (not chibi, not cartoon). {POSE}')


def submit(name):
    side, desc = TROOPS[name]
    style = STYLE_BLUE if side == 'blue' else STYLE_RED
    inputs = [api.upload_file(S1C), api.upload_file(style)]
    body = {'model': 'banana_pro', 'inputs': inputs, 'prompt': prompt(side, desc), 'aspect_ratio': '1:1', 'output_format': 'png'}
    return api.request_json('POST', '/generation/image-to-image', body)['data']['task_id']


def main():
    force = '--force' in sys.argv
    todo = [n for n in (a for a in sys.argv[1:] if not a.startswith('--'))] or list(TROOPS)
    todo = [n for n in todo if force or not (REFS / f'{n}.png').exists()]
    running = {}
    while todo or running:
        while todo and len(running) < 2:
            n = todo[0]
            try:
                running[n] = submit(n); todo.pop(0); print(time.strftime('%H:%M:%S'), n, 'submitted', flush=True)
            except Exception as e:
                if '429' in str(e) or 'limit' in str(e).lower(): break
                print(n, 'ERROR', str(e)[:200], flush=True); todo.pop(0)
        time.sleep(10)
        for n, tid in list(running.items()):
            d = api.task(tid); s = d.get('status')
            if s == 'success':
                u = d['output'].get('generated_image_url') or d['output'].get('image_url')
                urllib.request.urlretrieve(u, REFS / f'{n}.png'); del running[n]
                print(time.strftime('%H:%M:%S'), n, 'ok', flush=True)
            elif s in ('failed', 'cancelled', 'banned', 'expired'):
                print(n, 'FAILED', s, d.get('error_msg'), flush=True); del running[n]


if __name__ == '__main__':
    main()

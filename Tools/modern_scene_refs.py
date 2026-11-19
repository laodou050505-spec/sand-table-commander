"""现代时代布景件的参考图（11-13，样图 A）：10 件现代军事设施，替换中古的人造物（对应表见 PropBuilder.Modern）。
banana_pro 图生图（样图 A 只当风格参考）→ ArtSource/Refs/<名>/image.png，并在 ArtSource/specs.json 里登记面数上限；
之后照常走 P2：Blender -b -P Tools/p2_pipeline.py -- run <名…> → 看白模审计图 approve → p2_until_done.sh → future_tex_fix 不用跑（现代的不发光）。
用法：HTTPS_PROXY=http://127.0.0.1:7897 python3 Tools/modern_scene_refs.py [名…]（已有的跳过；--force 重出）"""
import json, pathlib, sys, time, urllib.request

sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api

PROJ = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable')
REFS = PROJ / 'ArtSource/Refs'
CONCEPT = PROJ / 'Docs/Concepts/modern/ma_blueshield_vs_redsand.png'
STYLE = ('Use the reference image ONLY for art style: premium hand-painted 28mm tabletop miniature scenery, matte paint with washes and edge highlights, soft studio light. '
         'Plain light-grey seamless background, three-quarter view from slightly above. ONE single scenery piece centered, whole object visible, sitting on a thin flat base slab. '
         'Thick solid closed shapes suitable for 3D modeling: no thin wires, no thin antennas, no cables, no fences, no netting, no transparent glass. '
         'Modern-day and realistic, NOT science fiction. No text, no letters, no numbers, no real-world insignia, no people, no vehicles other than described.')
PROPS = {   # 名: (面数上限, 描述)
    'M_Command': (14000, 'a small field command post: a low grey concrete bunker building with a flat roof, a navy-blue steel door, one navy-blue stripe painted around the wall, small shuttered window slits, a chunky white satellite dish and one short thick mast on the roof, tan sandbags stacked at one corner'),
    'M_Barracks': (12000, 'a prefabricated cabin barracks: a long rectangular navy-blue and light-grey insulated metal cabin with a slightly pitched corrugated roof, one door with two steps, two small windows with closed shutters, a boxy air-conditioner unit on one end, one bright yellow-green stripe along the wall'),
    'M_Watchtower': (14000, 'a tall concrete guard tower: a square grey concrete shaft, about three times taller than wide, with a wider navy-blue observation cabin on top with dark window openings and a flat overhanging roof, a chunky searchlight on the roof, a solid concrete external stair block at the base, one yellow-green stripe under the cabin'),
    'M_Crates': (10000, 'a compact pile of supplies: three olive-green wooden ammunition crates with rope handles stacked in two layers, two navy-blue steel oil drums standing next to them and one yellow-green jerry can, all touching each other'),
    'M_Truck': (14000, 'a military cargo truck, parked, no driver: a boxy navy-blue cab with dark windows and a light-grey bumper, a cargo bed covered by a light-grey canvas tarp, six chunky black rubber wheels, one bright yellow-green stripe on the cab door, side-on three-quarter view'),
    'M_Sandbags': (8000, 'one straight segment of a sandbag wall: three rows of tightly stacked tan burlap sandbags, low and wide, three times wider than tall, as thick as two sandbags'),
    'M_Container': (10000, 'two rusty shipping containers stacked: a rust-red corrugated steel container on the ground and a sand-yellow one stacked on top, slightly offset, with dents, rust streaks and closed doors, one rough stripe of red paint on the side'),
    'M_Barricade': (9000, 'a road barricade segment: two grey concrete jersey barriers standing side by side in a line, a stack of three black rubber tyres leaning against one end, a rusty sheet-metal plate bolted along the top, wider than tall'),
    'M_Target': (9000, 'a shooting range target set: two thick wooden A-frame stands side by side, each holding a round target board painted with red and white rings, with one low row of tan sandbags in front of them'),
    'M_Generator': (10000, 'a towed generator with floodlights: a boxy navy-blue diesel generator on a small two-wheel trailer with a tow bar, one short thick telescopic mast with a block of four square floodlights on top, a yellow-green stripe and a jerry can strapped to the side'),
    # 11-13 用户：“注意地面的样子和场景的布置” → 现代时代里还留着中古的骨帐 / 条纹圆帐 / 兽骨图腾，换成军帐和皮卡（这三件不要底板：NOBASE）
    'M_Tent_Sand': (9000, 'a military canvas wall tent of desert mercenaries: an olive-drab and khaki heavy canvas ridge tent, rectangular, twice as long as wide, slightly sagging roof, closed door flaps on the short end, a wide strip of faded red cloth tied over the ridge, a row of tan sandbags weighing down the bottom edge, patched and dusty'),
    'M_Tent_Blue': (9000, 'a tactical unit field tent: a clean light-grey heavy canvas ridge tent, rectangular, twice as long as wide, with a navy-blue band around the bottom third and one bright yellow-green stripe above it, closed door flaps on the short end, a navy-blue roof ridge cap, neat and tidy'),
    'M_Pickup': (13000, 'a battered desert pickup truck of mercenaries, parked, no driver: a sand-tan rusty 4x4 pickup with dark windows, a dented bonnet, a roll bar and stacked jerry cans and a spare tyre in the cargo bed, a red cloth strip tied to the roll bar, one rough red painted stripe on the door, four chunky black off-road wheels, side-on three-quarter view, front of the truck pointing to the right'),
}
NOBASE = {'M_Tent_Sand', 'M_Tent_Blue', 'M_Pickup'}


def query(tid):
    try:
        req = urllib.request.Request(f'https://api.tripo3d.ai/v2/openapi/task/{tid}', headers={'Authorization': f'Bearer {api.api_key()}', 'User-Agent': 'wartable-modern/1.0 (+node-fetch compatible)'})
        with urllib.request.urlopen(req, timeout=40) as r: return json.loads(r.read())['data']
    except Exception:
        return api.task(tid)


def main():
    force = '--force' in sys.argv
    todo = [a for a in sys.argv[1:] if not a.startswith('--')] or list(PROPS)
    todo = [n for n in todo if force or not (REFS / n / 'image.png').exists()]
    sp = PROJ / 'ArtSource/specs.json'; spec = json.loads(sp.read_text())
    for n, (faces, _) in PROPS.items(): spec.setdefault(n, {'faces': faces})
    sp.write_text(json.dumps(spec, ensure_ascii=False, indent=1))
    token = None; running = {}
    while todo or running:
        while todo and len(running) < 1:
            n = todo[0]
            try:
                token = token or api.upload_file(CONCEPT)
                body = {'model': 'banana_pro', 'inputs': [token], 'prompt': f'{STYLE if n not in NOBASE else STYLE.replace("sitting on a thin flat base slab", "NO base, NO ground slab, NO plinth under it")} The piece: {PROPS[n][1]}.', 'aspect_ratio': '1:1', 'output_format': 'png'}
                running[n] = api.request_json('POST', '/generation/image-to-image', body)['data']['task_id']; todo.pop(0); print(time.strftime('%H:%M:%S'), n, 'submitted', flush=True)
            except Exception as e:
                if '429' in str(e) or 'limit' in str(e).lower(): break
                if any(k in str(e) for k in ('EOF', 'reach', 'timed out', 'urlopen', 'Connection', 'SSL')): print(time.strftime('%H:%M:%S'), n, '断线，稍后重试', flush=True); time.sleep(20); break
                print(n, 'ERROR', str(e)[:200], flush=True); todo.pop(0)
        time.sleep(8)
        for n, tid in list(running.items()):
            try: d = query(tid)
            except Exception as e: print(n, 'query', str(e)[:80], flush=True); continue
            s = d.get('status')
            if s == 'success':
                o = d['output']; u = o.get('generated_image_url') or o.get('generated_image') or o.get('image_url') or o.get('image')
                (REFS / n).mkdir(parents=True, exist_ok=True)
                (REFS / n / 'image.png').write_bytes(urllib.request.urlopen(urllib.request.Request(u, headers={'User-Agent': 'wartable-modern/1.0'}), timeout=120).read()); del running[n]
                print(time.strftime('%H:%M:%S'), n, 'ok', flush=True)
            elif s in ('failed', 'cancelled', 'banned', 'expired'):
                print(n, 'FAILED', s, d.get('error_msg'), flush=True); del running[n]


if __name__ == '__main__':
    main()

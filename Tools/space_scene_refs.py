"""深空风格布景件的参考图（11-13，样图 C：小行星战场）：15 件，对应表见 PropBuilder.Space。banana_pro 纯文字出图（不带样图、不要底板）
→ ArtSource/Refs/<名>/image.png，并在 ArtSource/specs.json 里登记（src=raw、bake=nearest：直接用 Tripo 原始模型修复，见交接文档第十七节）；之后走 P2。
用法：HTTPS_PROXY=http://127.0.0.1:7897 python3 Tools/space_scene_refs.py [名…]（已有的跳过；--force 重出）"""
import json, pathlib, sys, time, urllib.request

sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api

PROJ = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable')
REFS = PROJ / 'ArtSource/Refs'
CONCEPT = PROJ / 'Docs/Concepts/modern/ma_blueshield_vs_redsand.png'
STYLE = ('Premium hand-painted 28mm tabletop miniature scenery, matte paint with washes and edge highlights, soft studio light. '
         'Plain light-grey seamless background, three-quarter view from slightly above. ONE single scenery piece centered, whole object visible, NO base, NO ground slab, NO plinth under it. '
         'Thick solid closed chunky shapes suitable for 3D modeling: no thin wires, no thin antennas, no cables, no fences, no netting, no transparent glass, no open lattice. '
         'Heavy industrial deep-space science fiction in an original design. No text, no letters, no numbers, no logos, no people, no vehicles other than described.')
BLUE = 'thick steel-blue and white enamel armour plating with orange hazard stripes and small pale-cyan glowing lights'
RED = 'scorched red and charcoal-black welded steel plates, soot stains, rust, and small round amber lamps'
PROPS = {   # 名: (面数上限, 描述)
    'S_Command': (14000, f'a fortified command module: a squat octagonal bunker building with sloped armoured walls, one heavy blast door, a chunky radar dish on the flat roof and two boxy vents, {BLUE}'),
    'S_Hab': (12000, f'a prefabricated habitat module: a long horizontal half-cylinder barracks pod lying on short thick landing skids, a round airlock door on one end, two small round portholes with shutters on the side, {BLUE}'),
    'S_Tower': (13000, f'a tall landing beacon tower: a thick square tapering armoured pylon about three times taller than wide, a boxy sensor cabin on top with a ring of pale-cyan beacon lights and a short chunky dish, {BLUE}'),
    'S_Pod': (10000, f'a drop pod shelter: a squat cone-shaped landing capsule standing on four thick folding petal legs, one petal door folded down as a ramp, a blunt nose cap on top, {BLUE}'),
    'S_Hauler': (13000, f'a heavy six-wheeled cargo hauler rover, parked, no driver: a boxy armoured cab with dark window slits at the front, a flat cargo bed loaded with two large crates, six chunky solid wheels, side-on three-quarter view with the front pointing right, {BLUE}'),
    'S_Crates': (9000, f'a compact stack of space cargo: three chunky rectangular cargo crates with reinforced corners stacked in two layers and two fat cylindrical fuel canisters standing next to them, all touching each other, {BLUE}'),
    'S_Barrier': (8000, f'one straight segment of a low blast barrier wall: a thick wedge-profile armoured wall, three times wider than tall, with black-and-orange hazard chevrons on the face and two bolted buttresses, {BLUE}'),
    'S_Lamp': (8000, f'a landing floodlight pylon: a short thick square post on a heavy cross-shaped foot, with a big boxy lamp head with four square pale-cyan glowing lights on top, {BLUE}'),
    'S_Wreck': (15000, 'a crashed starship hull section half buried at an angle: a huge broken cylindrical fuselage piece of dull grey armour plates with a torn jagged open end showing thick deck ribs inside, two big round engine nozzles, scorch marks, a few small amber emergency lights still glowing, rock rubble piled against it'),
    'S_Ribs': (9000, 'starship wreckage: three thick curved broken hull ribs of dull grey scorched metal sticking up out of a low pile of rubble like a ribcage, with one bent armour plate still attached'),
    'S_Derrick': (14000, f'a mining drill derrick: a tall thick four-legged tower with solid plate-covered legs, a huge vertical drill column in the middle going into a rock mound, a boxy winch house on top, {RED}'),
    'S_Shack': (10000, f'a miner pressure shack: a low boxy hut welded together from mismatched hull plates with a sloped roof, one small round window glowing amber, a thick hatch door, an exhaust pipe, {RED}'),
    'S_ScrapWall': (9000, f'a scrap barricade segment: a wall of overlapping welded hull plates and a big gear wheel propped up by thick steel beams, with two blunt spikes on top, wider than tall, {RED}'),
    'S_OreCart': (9000, f'a heavy ore hopper cart on two short caterpillar tracks, piled high with chunky rocks of glowing orange ember ore, with a tow bar and an amber lamp, {RED}'),
    'S_Spire': (9000, 'a natural asteroid rock spire: a tall jagged cluster of three dark charcoal-grey basalt rock shards leaning together, with rust-orange mineral veins and a little grey dust at the foot, no plants, no moss'),
}
NOBASE = set(PROPS)


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
    for n, (faces, _) in PROPS.items(): spec.setdefault(n, {'faces': faces, 'src': 'raw', 'bake': 'nearest'})
    sp.write_text(json.dumps(spec, ensure_ascii=False, indent=1))
    token = None; running = {}
    while todo or running:
        while todo and len(running) < 1:
            n = todo[0]
            try:
                body = {'model': 'banana_pro', 'prompt': f'{STYLE} The piece: {PROPS[n][1]}.', 'aspect_ratio': '1:1', 'output_format': 'png'}
                running[n] = api.request_json('POST', '/generation/text-to-image', body)['data']['task_id']; todo.pop(0); print(time.strftime('%H:%M:%S'), n, 'submitted', flush=True)
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

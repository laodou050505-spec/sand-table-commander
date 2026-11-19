"""封面主角（深空重甲骑士）按结构拆部件的参考图（11-18 用户：模型按结构分部件生成，图的细节要精，先图 → P2 白模 → 查白模 → 再贴图）。
整体设计图：ArtSource/Hero/design/front.png（T 字姿势正面）+ 四视图。这里以它为参考，用 banana_pro 图生图把每个部件单独画成一张精细的图：
  → ArtSource/Refs/Hero_<部件>/image.png（Tools/p2_pipeline.py 的输入），同时把条目写进 ArtSource/specs.json。
左右对称的部件（上臂、前臂、大腿、小腿）只做左边一件，装配时镜像。
用法：HTTPS_PROXY=http://127.0.0.1:7897 python3 Tools/hero_parts.py [部件...]（已有的跳过；--force 重出）。一次一张。"""
import pathlib, sys, time, json, urllib.request
sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api
R = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable/ArtSource'); DESIGN = R / 'Hero/design/front.png'
HEAD = ('From the armoured character in the reference image, draw ONLY ONE isolated component as a complete solid object for 3D modelling: ')
TAIL = (' Exactly the same design, colours and battle-worn painted grey-blue metal as the reference. Every opening where it joins other body parts is closed with a dark recessed solid joint cap (not hollow). '
        'Straight front view, the whole object visible, centred and filling most of the frame, pure white background, soft even studio light, no shadow. '
        'Extremely fine detail: crisp panel lines, rivets, bolts, layered plates, scratches, chipped paint edges. One single object only, no other body parts, no stand, no text, no numbers, no logos.')
PARTS = {   # 名字: (描述, 面数上限)
    'Head': ('the HELMET: a rounded dome helmet with one large round porthole visor glowing pale cyan, small bolts around the visor rim, a short white block crest on top, a thick neck ring collar at the bottom.', 16000),
    'Torso': ('the TORSO ARMOUR (chest, back and belly in one piece, without head, arms or legs): layered chest plates, a high collar ring on top, ribbed dark bellows around the abdomen, small pistons at the sides; the neck, both shoulder sockets and the waist are closed with dark caps.', 30000),
    'Pelvis': ('the PELVIS / WAIST piece: a heavy belt ring with a central codpiece plate and a round ball joint on each side for the legs; compact and symmetric; no thigh plates, no legs.', 10000),
    'UpperArm': ('ONE SHOULDER AND UPPER ARM: a big boxy pauldron block with an orange-and-black hazard stripe, mounted on a short thick ribbed upper-arm cylinder that ends in a round elbow joint disc; hanging straight down vertically.', 12000),
    'Forearm': ('ONE FOREARM WITH GAUNTLET: a thick armoured forearm guard with a white cuff ring at the wrist and a heavy armoured gauntlet hand clenched into a solid fist; pointing straight down vertically, elbow at the top, fist at the bottom.', 14000),
    'Thigh': ('ONE UPPER LEG: a large layered hip-and-thigh armour plate over a thick ribbed dark bellows thigh, ending in a round knee joint disc at the bottom; vertical.', 12000),
    'Shin': ('ONE LOWER LEG WITH BOOT: a heavy shin guard with a knee cap plate on top and a big blocky armoured boot with a thick sole at the bottom; vertical, toe pointing toward the viewer.', 14000),
    'Standard': None,
    'Rock': None,
}
SOLO = {   # 不属于角色身上的两件：文生图
    'Standard': ('ONE tall military war-standard pole for a heavy sci-fi knight, as a single solid object for 3D modelling: a thick gunmetal pole with brass rings, a sturdy horizontal crossbar near the top for hanging a banner, and on the very top a brass finial shaped like a radiant sun disc with short pointed rays; a spiked steel foot at the bottom. No cloth, no flag, no banner, no ropes, no tassels. '
                 'Battle-worn painted metal, crisp details, rivets and scratches. Straight front view, the whole pole visible from top to bottom, centred, pure white background, soft even studio light, no shadow. One single object, no text, no logos.', 8000),
    'Rock': ('ONE compact chunk of scorched dark volcanic battlefield rock as a base for a statue, as a single solid closed object for 3D modelling: wide and low with a roughly flat top surface, fractured angular sides, a few cracks glowing faint ember orange, scattered small embedded metal shrapnel. No grass, no plants, no loose floating stones, no figure. '
             'Realistic, highly detailed. Three-quarter view slightly from above, the whole rock visible, centred, pure white background, soft even studio light, no shadow. One single object, no text.', 10000),
}
UA = {'User-Agent': 'hero-parts/1.0 (+node-fetch compatible)'}
def query(tid):
    try:
        req = urllib.request.Request(f'https://api.tripo3d.ai/v2/openapi/task/{tid}', headers={'Authorization': f'Bearer {api.api_key()}', **UA})
        with urllib.request.urlopen(req, timeout=40) as r: return json.loads(r.read())['data']
    except Exception:
        return api.task(tid)
def retry(fn, n=6):
    for i in range(n):
        try: return fn()
        except Exception as e: print('  重试', i + 1, str(e)[:120], flush=True); time.sleep(8 + i * 6)
    raise RuntimeError('多次失败')
def main():
    force = '--force' in sys.argv; want = [a for a in sys.argv[1:] if not a.startswith('--')] or list(PARTS)
    specs = json.loads((R / 'specs.json').read_text()); tok = None
    for n in want:
        out = R / 'Refs' / f'Hero_{n}' / 'image.png'
        faces = (PARTS[n] or SOLO[n])[1]
        specs.setdefault(f'Hero_{n}', {'faces': faces, 'hard': 32})
        if out.exists() and not force: print('已有', n, flush=True); continue
        if PARTS[n]:
            prompt = HEAD + PARTS[n][0] + TAIL; assert len(prompt) <= 1800, (n, len(prompt))
            tok = tok or retry(lambda: api.upload_file(DESIGN))
            body = {'model': 'banana_pro', 'inputs': [tok], 'prompt': prompt, 'aspect_ratio': '1:1', 'output_format': 'png'}
            tid = retry(lambda: api.request_json('POST', '/generation/image-to-image', body)['data']['task_id'])
        else:
            prompt = SOLO[n][0]; assert len(prompt) <= 1800, (n, len(prompt))
            body = {'model': 'banana_pro', 'prompt': prompt, 'aspect_ratio': '3:4' if n == 'Standard' else '1:1', 'output_format': 'png'}
            tid = retry(lambda: api.request_json('POST', '/generation/text-to-image', body)['data']['task_id'])
        t0 = time.time(); url = None
        while time.time() - t0 < 300:
            time.sleep(6); d = retry(lambda: query(tid)); st = d.get('status')
            if st == 'success':
                o = d.get('output') or {}; url = o.get('generated_image_url') or o.get('generated_image') or next((v for v in o.values() if isinstance(v, str) and v.startswith('http')), None); break
            if st in ('failed', 'banned', 'cancelled', 'expired'): print('  失败', n, st, str(d)[:200], flush=True); break
        if not url: print('没出图', n, flush=True); continue
        out.parent.mkdir(parents=True, exist_ok=True)
        data = retry(lambda: urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=90).read()); out.write_bytes(data)
        print('OK', n, len(data), round(time.time() - t0), 's', flush=True)
    (R / 'specs.json').write_text(json.dumps(specs, indent=1, ensure_ascii=False))
    print('ALL DONE', flush=True)
main()

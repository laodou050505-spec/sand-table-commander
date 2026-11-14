"""深空风格地表贴图的素材（11-13，样图 C：小行星）：Tripo seedream_v5 俯拍的沙盘地表照片 → ArtSource/Space/ground/<名>_src.jpg，
之后 Tools/make_space_ground.py 做成可平铺贴图。Tripo 生图一次只能跑一张（429 就等），主域名经代理会断线（重试）。
用法：HTTPS_PROXY=http://127.0.0.1:7897 python3 Tools/space_ground_gen.py [名字...]（已有的跳过）"""
import pathlib, sys, time, json, urllib.request
sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api

OUT = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable/ArtSource/Space/ground')
HEAD = 'Top-down orthographic photo of a tabletop wargame diorama ground surface, filling the whole frame edge to edge: '
TAIL = ' Uniform everywhere, no objects, no painted lines, no text, no shadows, no horizon, flat even soft lighting, seamless texture, highly detailed miniature terrain, matte.'
JOBS = {
    'regolith': 'dark charcoal-grey asteroid regolith: coarse dust and gravel with many tiny impact pits and a few small shallow craters, scattered sharp dark rock chips, faint streaks of rust-orange mineral dust.',
    'ore': 'rust-orange and ochre iron-ore dust and crushed ore gravel on an asteroid, with dark slag chips and a few dull metallic nuggets, dry and powdery.',
    'deck': 'a heavy industrial steel deck road made of large rectangular diamond-tread plates bolted edge to edge in a regular grid, dark gunmetal grey, worn bare metal in the middle, rust and dust in the seams, a few faded scuffed scratches, no stripes.',
}

def query(tid):
    try:
        req = urllib.request.Request(f'https://api.tripo3d.ai/v2/openapi/task/{tid}', headers={'Authorization': f'Bearer {api.api_key()}', 'User-Agent': 'ground-gen/1.0'})
        with urllib.request.urlopen(req, timeout=40) as r: return json.loads(r.read())['data']
    except Exception:
        d = api.task(tid); return d.get('data', d)

for n in [a for a in sys.argv[1:]] or list(JOBS):
    dst = OUT / f'{n}_src.jpg'
    if dst.exists(): continue
    OUT.mkdir(parents=True, exist_ok=True)
    tid = None
    while tid is None:
        try: tid = api.request_json('POST', '/generation/text-to-image', {'model': 'seedream_v5', 'prompt': HEAD + JOBS[n] + TAIL, 'size': '2K'})['data']['task_id']
        except Exception as e: print(time.strftime('%H:%M:%S'), n, '等待', str(e)[:60], flush=True); time.sleep(25)
    print(time.strftime('%H:%M:%S'), n, 'submitted', flush=True)
    while True:
        time.sleep(8)
        try: d = query(tid)
        except Exception: continue
        if d.get('status') == 'success':
            o = d['output']; u = o.get('generated_image_url') or o.get('generated_image') or o.get('image_url') or o.get('image')
            dst.write_bytes(urllib.request.urlopen(urllib.request.Request(u, headers={'User-Agent': 'ground-gen/1.0'}), timeout=120).read()); print(n, 'ok', flush=True); break
        if d.get('status') in ('failed', 'cancelled', 'banned', 'expired'): print(n, 'FAILED', d.get('status'), flush=True); break

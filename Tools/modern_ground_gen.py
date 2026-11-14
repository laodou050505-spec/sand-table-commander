"""现代时代地表贴图的素材（11-13 用户：注意地面的样子）：Tripo seedream_v5 俯拍的沙盘地表照片 → ArtSource/Modern/ground/<名>_src.jpg，
之后 Tools/make_modern_ground.py 做成可平铺贴图。Tripo 生图一次只能跑一张（429 就等），主域名经代理会断线（重试）。
用法：HTTPS_PROXY=http://127.0.0.1:7897 python3 Tools/modern_ground_gen.py [名字...]（已有的跳过）"""
import pathlib, sys, time, json, urllib.request
sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api

OUT = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable/ArtSource/Modern/ground')
HEAD = 'Top-down orthographic photo of a tabletop wargame diorama ground surface, filling the whole frame edge to edge: '
TAIL = ' Uniform everywhere, no objects, no painted lines, no text, no shadows, no horizon, flat even soft lighting, seamless texture, highly detailed miniature terrain, matte.'
JOBS = {
    'steppe': 'dry late-summer steppe grass made of static flock, muted olive green mixed with khaki and straw-yellow tufts, small patches of bare tan earth showing through, a few tiny pebbles.',
    'gravel': 'a compacted military gravel road surface, grey-beige crushed stone and packed dry dirt, fine grit, scattered small stones, faint criss-crossing tyre tread marks in random directions, a few shallow dry potholes, no grass.',
    'camp': 'bare dry trampled earth of a military camp, tan and light brown hard-packed dirt with fine cracks, dust, sparse dead grass stubble and tiny pebbles.',
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

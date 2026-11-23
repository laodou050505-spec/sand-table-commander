"""把旗牌上的兵种图标（20 个）和兵法页的示意图（4 个）从毛笔味重绘成铜版画 / 木刻纹章的味道（11-17 用户：全套界面西式奇幻，不要中国风）。
做法：原图当参考，banana_pro 图生图——题材、姿势、构图不变，只换画法（实心黑形 + 里面刻白色排线，边缘干净，白底）。
出图到 ArtSource/UI/gen/<名>_e/01_generated_image_url.png；make_field_ui.py 的 unit_mask 有 _e 就用 _e，没有就用原图。
用法：HTTPS_PROXY=http://127.0.0.1:7897 python3 Tools/engrave_icons.py [名字...]（已有的跳过；--force 重出）。一次一张（Tripo 生图排队，不并发）。"""
import pathlib, sys, time, json, urllib.request
sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api
G = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable/ArtSource/UI/gen')
NAMES = [f'unit_{g}{s}' for s in ('', '_g', '_f', '_m', '_s') for g in ('inf', 'arch', 'cav', 'ha')] + ['tac_anvil', 'tac_focus', 'tac_general', 'tac_wall']
PROMPT = ('Redraw this exact emblem as an antique copperplate engraving / woodcut heraldic emblem. Keep the SAME subject, the same objects, the same pose and the same composition. '
          'Bold solid black shapes with crisp clean edges; interior detail carved as fine white engraved hatching and contour lines. '
          'No brush strokes, no dry-brush texture, no ink splatter, no grey wash, no gradients. Pure black ink on a plain pure white background, '
          'centered with a generous empty margin all around. One single emblem only. No border, no frame, no circle around it, no text, no letters, no numbers.')

HAND = ('Redraw this exact hand gesture illustration as an antique copperplate engraving, like a plate from a 19th century manual. Keep the SAME hands, the same gesture, '
        'the same objects and arrows, the same composition. Clean confident black contour lines with fine engraved hatching for shading; elegant, anatomically correct hands. '
        'No brush strokes, no ink splatter, no grey wash. Pure black ink on a plain pure white background, centered with a generous empty margin. '
        'No border, no frame, no dark blot behind it, no text, no letters, no numbers.')

def query(tid):
    try:
        req = urllib.request.Request(f'https://api.tripo3d.ai/v2/openapi/task/{tid}', headers={'Authorization': f'Bearer {api.api_key()}', 'User-Agent': 'engrave-icons/1.0 (+node-fetch compatible)'})
        with urllib.request.urlopen(req, timeout=40) as r: return json.loads(r.read())['data']
    except Exception:
        return api.task(tid)

def retry(fn, n=6):
    for i in range(n):
        try: return fn()
        except Exception as e:
            print('  重试', i + 1, str(e)[:120], flush=True); time.sleep(8 + i * 6)
    raise RuntimeError('多次失败')

def main():
    force = '--force' in sys.argv; want = [a for a in sys.argv[1:] if not a.startswith('--')] or NAMES
    for n in want:
        src = G / n / '01_generated_image_url.png'; out = G / (n + '_e') / '01_generated_image_url.png'
        if not src.exists() and (G / (n + '.png')).exists(): src = G / (n + '.png')      # 手的图标原稿是单个文件
        if not src.exists(): print('缺原图', n, flush=True); continue
        if out.exists() and not force: print('已有', n, flush=True); continue
        tok = retry(lambda: api.upload_file(src))
        body = {'model': 'banana_pro', 'inputs': [tok], 'prompt': (HAND if n.startswith('hand_') else PROMPT), 'aspect_ratio': '1:1', 'output_format': 'png'}
        tid = retry(lambda: api.request_json('POST', '/generation/image-to-image', body)['data']['task_id'])
        t0 = time.time(); url = None
        while time.time() - t0 < 300:
            time.sleep(6); d = retry(lambda: query(tid)); st = d.get('status')
            if st == 'success':
                o = d.get('output') or {}; url = o.get('generated_image_url') or o.get('generated_image') or next((v for v in o.values() if isinstance(v, str) and v.startswith('http')), None); break
            if st in ('failed', 'banned', 'cancelled', 'expired'): print('  失败', n, st, str(d)[:200], flush=True); break
        if not url: print('没出图', n, flush=True); continue
        out.parent.mkdir(parents=True, exist_ok=True)
        req = urllib.request.Request(url, headers={'User-Agent': 'engrave-icons/1.0 (+node-fetch compatible)'})
        data = retry(lambda: urllib.request.urlopen(req, timeout=90).read()); out.write_bytes(data)
        print('OK', n, len(data), round(time.time() - t0), 's', flush=True)
    print('ALL DONE', flush=True)
main()

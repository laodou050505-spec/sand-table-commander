"""深空风格的界面图（11-13，样图 C）：4 张旗牌水墨图标 + 2 面帅旗。做法同 Tools/modern_ui_gen.py。
  图标 → ArtSource/UI/gen/unit_<inf|arch|cav|ha>_s/；帅旗 → ArtSource/Banners/standard_lancer.png（星枪团）、standard_ember.png（余烬矿帮）
用法：HTTPS_PROXY=http://127.0.0.1:7897 python3 Tools/space_ui_gen.py [名…]"""
import io, json, pathlib, sys, time, urllib.request

sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api

PROJ = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable')
INK = ('A single bold black ink-brush emblem on plain clean white paper, centered, lots of empty margin. Very simple and bold like a stamped seal or a chess-piece icon: '
       'thick solid black shapes with only a few wide white gaps, compact overall shape, dry-brush ink edges. Pure black ink only, no color, no grey wash, no thin lines, '
       'no text, no letters, no frame, no border, no background objects, no paper stains. The emblem: ')
FLAG = (' Flat front-on view of the whole cloth filling the entire frame edge to edge, no pole, no background, no perspective, no folds, no shadows, no text, no letters, '
        'no real-world flag or insignia, crisp clean shapes, rich saturated colors, slightly woven fabric texture, hand-painted miniature heraldry style.')
JOBS = {
    'unit_inf_s': ('seedream_v5', INK + 'a tall heavy rectangular armoured door shield with one horizontal slit near the top and three diagonal stripes at the bottom, seen from the front and slightly turned, with one big blocky hammer crossing diagonally behind it.', PROJ / 'ArtSource/UI/gen/unit_inf_s/01_generated_image_url.png'),
    'unit_arch_s': ('seedream_v5', INK + 'the side silhouette of one long heavy boxy rail rifle with a thick rectangular twin-rail barrel, a blocky stock and a square scope, pointing left, tilted slightly upward.', PROJ / 'ArtSource/UI/gen/unit_arch_s/01_generated_image_url.png'),
    'unit_cav_s': ('seedream_v5', INK + 'the side silhouette of a bulky armoured mech-suit rider sitting on a blocky wedge-shaped hover sled with no wheels, facing left, holding a long thick lance leveled forward.', PROJ / 'ArtSource/UI/gen/unit_cav_s/01_generated_image_url.png'),
    'unit_ha_s': ('seedream_v5', INK + 'the side silhouette of a bulky armoured rider sitting on a small tank-like tracked crawler with caterpillar tracks and a big conical drill on the front, facing left, aiming a short thick gun forward.', PROJ / 'ArtSource/UI/gen/unit_ha_s/01_generated_image_url.png'),
    'standard_lancer': ('banana_pro', 'A deep-space mech regiment banner textile design: a steel-blue field, in the center a large white vertical lance head pointing up through a white orbit ring, a small pale-cyan four-pointed star above it, a bold orange-and-black hazard chevron band along the bottom edge, a thin white border.' + FLAG, PROJ / 'ArtSource/Banners/standard_lancer.png'),
    'standard_ember': ('banana_pro', 'An asteroid-miner rebel clan banner textile design: a scorched dark-red field with soot stains, in the center a large amber-orange glowing gear-toothed sun disc with two crossed black pickaxes over it, a rough black border with rivet dots, torn lower edge.' + FLAG, PROJ / 'ArtSource/Banners/standard_ember.png'),
}


def query(tid):
    try:
        req = urllib.request.Request(f'https://api.tripo3d.ai/v2/openapi/task/{tid}', headers={'Authorization': f'Bearer {api.api_key()}', 'User-Agent': 'wartable-modern/1.0 (+node-fetch compatible)'})
        with urllib.request.urlopen(req, timeout=40) as r: return json.loads(r.read())['data']
    except Exception:
        return api.task(tid)


def main():
    from PIL import Image
    force = '--force' in sys.argv
    todo = [a for a in sys.argv[1:] if not a.startswith('--')] or list(JOBS)
    todo = [n for n in todo if force or not JOBS[n][2].exists()]
    running = {}
    while todo or running:
        while todo and len(running) < 1:
            n = todo[0]; model, prompt, _ = JOBS[n]
            body = {'model': model, 'prompt': prompt, 'output_format': 'png'}
            if model == 'banana_pro': body.update(aspect_ratio='3:2', size='2K')
            else: body.update(size='2K', watermark=False)
            try:
                running[n] = api.request_json('POST', '/generation/text-to-image', body)['data']['task_id']; todo.pop(0); print(time.strftime('%H:%M:%S'), n, 'submitted', flush=True)
            except Exception as e:
                if '429' in str(e) or 'limit' in str(e).lower(): break
                if any(k in str(e) for k in ('EOF', 'reach', 'timed out', 'urlopen', 'Connection', 'SSL')): print(time.strftime('%H:%M:%S'), n, '断线，稍后重试', flush=True); time.sleep(20); break
                print(n, 'ERROR', str(e)[:300], flush=True); todo.pop(0)
        time.sleep(8)
        for n, tid in list(running.items()):
            try: d = query(tid)
            except Exception as e: print(n, 'query', str(e)[:80], flush=True); continue
            s = d.get('status')
            if s == 'success':
                o = d['output']; u = o.get('generated_image_url') or o.get('generated_image') or o.get('image_url') or o.get('image')
                raw = urllib.request.urlopen(urllib.request.Request(u, headers={'User-Agent': 'wartable-modern/1.0'}), timeout=120).read()
                dst = JOBS[n][2]; dst.parent.mkdir(parents=True, exist_ok=True)
                Image.open(io.BytesIO(raw)).convert('RGB').save(dst); del running[n]     # 统一存成真正的 PNG（接口有时回 JPEG）
                print(time.strftime('%H:%M:%S'), n, 'ok', flush=True)
            elif s in ('failed', 'cancelled', 'banned', 'expired'):
                print(n, 'FAILED', s, d.get('error_msg'), flush=True); del running[n]


if __name__ == '__main__':
    main()

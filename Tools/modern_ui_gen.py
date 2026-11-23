"""现代时代的界面图（11-13）：4 张旗牌水墨图标 + 2 面帅旗。断线自动重试，已有的跳过（--force 重出）。
  图标 → ArtSource/UI/gen/unit_<inf|arch|cav|ha>_m/01_generated_image_url.png（seedream_v5 文生图；之后跑 Tools/make_field_ui.py 出旗牌）
  帅旗 → ArtSource/Banners/standard_shield.png（蓝盾特勤）、standard_sand.png（赤砂佣兵）（banana_pro 文生图 3:2；之后跑 Tools/gothic_recolor.py 拷进工程并出结算小旗）
用法：HTTPS_PROXY=http://127.0.0.1:7897 python3 Tools/modern_ui_gen.py [名…]"""
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
    'unit_inf_m': ('seedream_v5', INK + 'a tall rectangular riot shield with a small window near the top, seen from the front and slightly turned, with one short straight baton crossing diagonally behind it.', PROJ / 'ArtSource/UI/gen/unit_inf_m/01_generated_image_url.png'),
    'unit_arch_m': ('seedream_v5', INK + 'the side silhouette of one modern rifle with a scope, a stock, a curved magazine and a long barrel, pointing left, tilted slightly upward.', PROJ / 'ArtSource/UI/gen/unit_arch_m/01_generated_image_url.png'),
    'unit_cav_m': ('seedream_v5', INK + 'the side silhouette of a helmeted rider crouched on a modern motorcycle with two round solid wheels, facing left, holding a long pole leveled forward like a lance.', PROJ / 'ArtSource/UI/gen/unit_cav_m/01_generated_image_url.png'),
    'unit_ha_m': ('seedream_v5', INK + 'the side silhouette of a helmeted rider on an off-road dirt bike with two knobby wheels doing a small wheelie, facing left, aiming a short rifle forward with one hand.', PROJ / 'ArtSource/UI/gen/unit_ha_m/01_generated_image_url.png'),
    'standard_shield': ('banana_pro', 'A modern security force banner textile design: a deep navy-blue field, a large light-grey heraldic shield emblem in the center with a bold bright yellow-green chevron across it and a small white five-pointed star above, a bold bright yellow-green border band with a thin white inner line, small light-grey chevrons in the corners.' + FLAG, PROJ / 'ArtSource/Banners/standard_shield.png'),
    'standard_sand': ('banana_pro', 'A desert mercenary band banner textile design: a sand-tan desert camouflage field, a large crimson-red rising sun disc in the center with a black scorpion silhouette on it, two crossed black machetes behind the disc, a bold dark-brown border band with a thin red inner line, faded dusty look.' + FLAG, PROJ / 'ArtSource/Banners/standard_sand.png'),
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

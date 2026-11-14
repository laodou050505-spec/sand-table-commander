"""哥特科幻时代的兵种（11-10 用户选定 s1c：蓝 = 圣钟骑士团，红 = 炉铁氏族；截止 11-15）。
做法照原型小兵（~/sandtable-xr/tools/tripo/generate.mjs）：定妆图 → 四视图 → P2 模型（带贴图）→ 审计 → 【人工审批】
→ Tripo 自动绑骨（biped + Mixamo 规范，rig v1.0-20240301，才能用 preset:biped:* 动作）→ 4 批预设动作重定向（每批 5 个）
→ 文件放进 ~/sandtable-xr/tools/tripo/raw/g_<兵种>/（model.glb / rig.glb / anim0..3.glb），之后由原型的 tools/build/build.ts 补出 50 段动作。

定妆图：ArtSource/Gothic/refs/<兵种>.png（白底 T 字姿势、不拿武器）。进度：ArtSource/Gothic/chars/<兵种>/state.json，可随时中断续跑。
用法（普通 python3）：
  python3 Tools/gothic_chars.py run [兵种...]        推进到“等待审批”或完成（循环等待）
  python3 Tools/gothic_chars.py step [兵种...]       只推进一轮
  python3 Tools/gothic_chars.py approve 兵种...      审计图看过、通过
  python3 Tools/gothic_chars.py reject 兵种... [--keep-mv]   重做模型（--keep-mv 只换种子）
  python3 Tools/gothic_chars.py status"""
import json, os, pathlib, shutil, subprocess, sys, time, urllib.request

sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api

PROJ = pathlib.Path('/Users/ld/Documents/ChatGPT/WarTable')
# 11-12：同一套流程也做未来时代——WT_ERA=future python3 Tools/gothic_chars.py …（定妆图 ArtSource/Future/refs，原型里叫 f_<兵种>）
ERA = os.environ.get('WT_ERA', 'gothic')
EDIR, PRE = {'gothic': ('Gothic', 'g_'), 'future': ('Future', 'f_'), 'modern': ('Modern', 'm_'), 'space': ('Space', 's_')}[ERA]   # 11-13：现代时代 WT_ERA=modern
REFS = PROJ / f'ArtSource/{EDIR}/refs'
OUT = PROJ / f'ArtSource/{EDIR}/chars'
RAW = pathlib.Path('/Users/ld/sandtable-xr/tools/tripo/raw')
BLENDER = '/Applications/Blender.app/Contents/MacOS/Blender'
V2 = 'https://api.tripo3d.ai/v2/openapi'
FACES = 5000   # 原型士兵约 5k 三角面（几百个同屏，近处 LOD0、远处 LOD1 30%）
ANIMS = [   # 和原型 assets.mjs 的 CHARACTER_ANIMATIONS 一致（build.ts 按名字取）
    ['preset:biped:idle', 'preset:biped:walk', 'preset:biped:run', 'preset:biped:chop', 'preset:biped:front_kick_01'],
    ['preset:biped:hit_to_body_01', 'preset:biped:hit_to_head', 'preset:biped:hit_to_stomach', 'preset:biped:defeat_03', 'preset:biped:fall'],
    ['preset:biped:pitch_baseball', 'preset:biped:sit', 'preset:biped:victory_celebration', 'preset:biped:cheer', 'preset:biped:climb'],
    ['preset:biped:box_02', 'preset:biped:hurt', 'preset:biped:golf', 'preset:biped:standing_relax', 'preset:biped:slash'],
]
DONE, BAD = ('success',), ('failed', 'cancelled', 'banned', 'expired', 'unknown')


class Busy(Exception):
    pass


def v2(method, path, body=None):
    req = urllib.request.Request(V2 + path, method=method, data=json.dumps(body).encode() if body else None,
                                 headers={'Authorization': f'Bearer {api.api_key()}', 'User-Agent': 'wartable-gothic/1.0 (+node-fetch compatible)', **({'Content-Type': 'application/json'} if body else {})})   # 默认的 Python-urllib UA 会被 Cloudflare 挡（403 / 1010）
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            j = json.loads(r.read())
    except urllib.error.HTTPError as e:
        txt = e.read().decode(errors='replace')
        if e.code == 429 or 'limit' in txt.lower():
            raise Busy()
        raise RuntimeError(f'v2 {method} {path}: {e.code} {txt[:300]}')
    if j.get('code') != 0:
        raise RuntimeError(f'v2 {method} {path}: {j}')
    return j['data']


def v3post(path, body):
    try:
        return api.request_json('POST', path, body)['data']['task_id']
    except Exception as e:
        if '429' in str(e) or 'limit' in str(e).lower():
            raise Busy()
        raise


def fetch(url, dst):
    for _ in range(6):
        try:
            req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0 (Macintosh) wartable-gothic/1.0'})   # 默认 UA 会被 CDN 挡（403）
            with urllib.request.urlopen(req, timeout=120) as r: dst.write_bytes(r.read())
            return True
        except Exception as e:
            print('download retry', dst.name, str(e)[:80], flush=True); time.sleep(6)
    return False


def url_of(out):
    for k in ('model', 'pbr_model', 'base_model', 'model_url', 'pbr_model_url'):
        if isinstance(out.get(k), str): return out[k]
    return None


def advance(name):
    folder = OUT / name; folder.mkdir(parents=True, exist_ok=True)
    sf = folder / 'state.json'
    st = json.loads(sf.read_text()) if sf.exists() else {}
    save = lambda: sf.write_text(json.dumps(st, indent=1, ensure_ascii=False))

    def poll3(key):
        d = api.task(st[key]); s = d.get('status')
        if s in DONE: st[key + '_result'] = d; save()
        return s

    def poll2(key):
        d = v2('GET', f'/task/{st[key]}'); s = d.get('status')
        if s in DONE: st[key + '_result'] = d; save()
        return s

    if st.get('done'):
        return 'done'
    # ---- 定妆图 → 四视图
    if 'mv' not in st:
        ref = REFS / f'{name}.png'
        if not ref.exists(): return 'no-ref'
        st['ref_token'] = st.get('ref_token') or api.upload_file(ref); save()
        st['mv'] = v3post('/generation/image-to-multiview', {'input': st['ref_token']}); save()
        return 'mv-submitted'
    if 'mv_result' not in st:
        s = poll3('mv')
        if s in BAD: del st['mv']; save(); return 'mv-failed(retry)'
        if s not in DONE: return f'mv-{s}'
        out = st['mv_result']['output']
        for k in ('front', 'left', 'back', 'right'):
            if out.get(k + '_view_url'): fetch(out[k + '_view_url'], folder / f'view_{k}.png')
        return 'mv-done'
    # ---- P2 模型（带贴图：绑骨要用生成任务本身的 ID，和原型一样）
    if 'model' not in st:
        body = {'inputs': [{'task_id': st['mv']}], 'model': 'P2-20260801', 'face_limit': FACES, 'texture': True, 'pbr': False,
                'model_seed': 20261110 + 1000 * st.get('retries', 0) + sum(map(ord, name))}
        st['model'] = v3post('/generation/multiview-to-model', body); save()
        return 'model-submitted'
    if 'model_result' not in st:
        s = poll3('model')
        if s in BAD: del st['model']; save(); return 'model-failed(retry)'
        if s not in DONE: return f'model-{s}'
        u = url_of(st['model_result']['output'])
        if not (u and fetch(u, folder / 'model.glb')): del st['model_result']; save(); return 'model-download-failed'
        return 'model-done'
    # ---- 审计（碎片 / 悬浮块 / 洞 / 非流形 / 自穿插 + 六视图），只跑一次
    if 'audit' not in st:
        r = subprocess.run([BLENDER, '-b', '--python', str(PROJ / 'Tools/mesh_audit_p2.py'), '--', str(folder / 'model.glb'), str(folder / 'audit'), name],
                           capture_output=True, text=True, timeout=1800)
        p = folder / 'audit' / f'{name}_audit.json'
        if not p.exists(): print(name, 'AUDIT FAILED', r.stdout[-400:], r.stderr[-400:]); return 'audit-error'
        st['audit'] = json.loads(p.read_text()); save()
        return 'audited ' + summary(st['audit'])
    if not (folder / 'approved').exists():
        return 'await-approval ' + summary(st['audit'])
    # ---- 绑骨（v2：animate_rig，biped + Mixamo）
    if 'rig' not in st:
        st['rig'] = v2('POST', '/task', {'type': 'animate_rig', 'original_model_task_id': st['model'], 'out_format': 'glb',
                                         'rig_type': 'biped', 'spec': 'mixamo', 'model_version': 'v1.0-20240301'})['task_id']; save()
        return 'rig-submitted'
    if 'rig_result' not in st:
        s = poll2('rig')
        if s in BAD: st['rig_error'] = st.pop('rig'); save(); return f'rig-{s}'
        if s not in DONE: return f'rig-{s}'
        if not fetch(url_of(st['rig_result']['output']), folder / 'rig.glb'): del st['rig_result']; save(); return 'rig-download-failed'
        return 'rig-done'
    if not (folder / 'rig.glb').exists() and not fetch(url_of(st['rig_result']['output']), folder / 'rig.glb'):
        return 'rig-download-failed'
    # ---- 4 批预设动作（原地、只导出骨骼动画）
    pending = []
    for i, anims in enumerate(ANIMS):
        k = f'anim{i}'
        if k not in st:
            st[k] = v2('POST', '/task', {'type': 'animate_retarget', 'original_model_task_id': st['rig'], 'out_format': 'glb', 'bake_animation': True,
                                         'export_with_geometry': False, 'animate_in_place': True, 'animations': anims})['task_id']; save()
            pending.append(k); continue
        if k + '_result' not in st:
            s = poll2(k)
            if s in BAD: del st[k]; save(); pending.append(k + '-failed'); continue
            if s not in DONE: pending.append(f'{k}-{s}'); continue
            if not fetch(url_of(st[k + '_result']['output']), folder / f'{k}.glb'): del st[k + '_result']; save(); pending.append(k + '-dl'); continue
    if pending:
        return 'anims ' + ' '.join(pending)
    # ---- 交给原型加工
    dst = RAW / f'{PRE}{name}'; dst.mkdir(parents=True, exist_ok=True)
    for f in ['model.glb', 'rig.glb'] + [f'anim{i}.glb' for i in range(len(ANIMS))]:
        shutil.copy2(folder / f, dst / f)
    st['done'] = True; save()
    return 'done → ' + str(dst)


def summary(a):
    return f"faces={a.get('faces')} islands={a.get('islands')} debris={a.get('debris')} floating={a.get('floating_islands')} holes={a.get('holes')} boundary={a.get('boundary_edges')} nonman={a.get('nonmanifold_edges')} selfX={a.get('self_intersections')}"


def names(args):
    return args or sorted(p.stem for p in REFS.glob('*.png'))


if __name__ == '__main__':
    cmd, args = sys.argv[1], [a for a in sys.argv[2:] if not a.startswith('--')]
    if cmd in ('approve',):
        for n in args: (OUT / n).mkdir(parents=True, exist_ok=True); (OUT / n / 'approved').touch(); print(n, 'approved')
    elif cmd == 'reject':
        for n in args:
            sf = OUT / n / 'state.json'; st = json.loads(sf.read_text()) if sf.exists() else {}
            keep = {'retries': st.get('retries', 0) + 1}
            if '--keep-mv' in sys.argv:
                for k in ('ref_token', 'mv', 'mv_result'): keep[k] = st.get(k)
            sf.write_text(json.dumps(keep, indent=1)); (OUT / n / 'approved').unlink(missing_ok=True)
            for f in ('model.glb',): (OUT / n / f).unlink(missing_ok=True)
            shutil.rmtree(OUT / n / 'audit', ignore_errors=True); print(n, 'reset, retries', keep['retries'])
    elif cmd == 'status':
        for n in names(args):
            sf = OUT / n / 'state.json'; st = json.loads(sf.read_text()) if sf.exists() else {}
            print(n, 'done' if st.get('done') else ('approved' if (OUT / n / 'approved').exists() else ''), [k for k in st if not k.endswith('_result') and k not in ('ref_token', 'audit')])
    else:
        todo = names(args)
        while True:
            busy = False; waiting = []
            for n in todo:
                try:
                    r = advance(n)
                except Busy:
                    r = 'busy(429)'; busy = True
                except Exception as e:
                    r = 'ERROR ' + str(e)[:200]
                print(time.strftime('%H:%M:%S'), n, r, flush=True)
                if not (r.startswith('done') or r.startswith('await') or r.startswith('no-ref') or r.startswith('ERROR') or r.startswith('rig-fail')):
                    waiting.append(n)
            if cmd == 'step' or not waiting: break
            time.sleep(20 if busy else 12)

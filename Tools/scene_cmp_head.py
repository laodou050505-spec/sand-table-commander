"""把当前场景和 git 里提交版的场景逐件对比（按“实例名 + 坐标”比位置 / 旋转 / 缩放）。
用途：改了 PropBuilder 之后确认定稿的中古 / 哥特布景一件没动（11-13：布景落点和 BoardMap.Pads 的坑，见 Docs/交接_1111_沙盘3.md 第十三节）。
用法：在工程根目录 python3 Tools/scene_cmp_head.py"""
import sys, re, subprocess
def load(txt):
    out = {}
    for blk in txt.split('--- !u!1001')[1:]:
        nm = re.search(r'propertyPath: m_Name\s*\n\s*value: (\S+)', blk)
        if not nm: continue
        pos = {}
        for ax in 'xyz':
            m = re.search(r'propertyPath: m_LocalPosition\.%s\s*\n\s*value: (\S+)' % ax, blk)
            pos[ax] = float(m.group(1)) if m else 0.0
        sc = re.search(r'propertyPath: m_LocalScale\.x\s*\n\s*value: (\S+)', blk)
        ry = re.search(r'propertyPath: m_LocalRotation\.y\s*\n\s*value: (\S+)', blk)
        out.setdefault((nm.group(1), round(pos['x'], 4), round(pos['z'], 4)), []).append((pos['y'], float(sc.group(1)) if sc else 1.0, float(ry.group(1)) if ry else 0.0))
    return out
head = load(subprocess.run(['git', 'show', (__import__('os').environ.get('CMP_REF', 'HEAD')) + ':Assets/_WarTable/Scenes/Board.unity'], capture_output=True, text=True).stdout)
now = load(open('Assets/_WarTable/Scenes/Board.unity').read())
same = diff = missing = 0; worst = []
for k, v in head.items():
    if k not in now: missing += 1; worst.append(('缺', k)); continue
    for a, b in zip(sorted(v), sorted(now[k])):
        d = max(abs(a[0] - b[0]), abs(a[1] - b[1]) * 0.01, abs(a[2] - b[2]) * 0.01)
        if d < 2e-6: same += 1
        else: diff += 1; worst.append((round(d * 1000, 3), k, round(a[0] * 1000, 2), round(b[0] * 1000, 2)))
print(f'提交版 {sum(len(v) for v in head.values())} 个实例：现在逐件一致 {same}，不一致 {diff}，找不到 {missing}；现在共 {sum(len(v) for v in now.values())} 个（多出来的是未来时代新增的）')
for w in worst[:12]: print('  ', w)

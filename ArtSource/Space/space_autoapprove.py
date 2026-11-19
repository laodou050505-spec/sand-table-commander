# 深空兵种白模自动放行：审计里 debris == 0、floating == 0、islands ≤ 120（机甲的活塞、铆钉本来就是分开的小块，11-13 看过狂战士 68 块是正常结构） 的直接批准；其余写进 /tmp/sp2/space_manual.txt 等人工看
import glob, json, os, time
D = '/Users/ld/Documents/ChatGPT/WarTable/ArtSource/Space/chars/'
manual = []
for d in sorted(glob.glob(D + '*')):
    n = os.path.basename(d); a = f'{d}/audit/{n}_audit.json'
    if not os.path.exists(a) or os.path.exists(d + '/approved'): continue
    j = json.load(open(a))
    if j.get('debris', 9) == 0 and j.get('floating_islands', 9) == 0 and j.get('islands', 999) <= 120:
        open(d + '/approved', 'w').close(); print(time.strftime('%H:%M:%S'), 'AUTO-APPROVED', n, 'islands', j.get('islands'), flush=True)
    else: manual.append(f"{n} islands={j.get('islands')} debris={j.get('debris')} floating={j.get('floating_islands')}")
open('/tmp/sp2/space_manual.txt', 'w').write('\n'.join(manual))

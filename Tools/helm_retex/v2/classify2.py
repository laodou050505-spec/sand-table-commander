"""双面战盔 v2 分区：按来源（骑士 / 兽人 / 狮子）分别聚类，再用位置规则分出角、骷髅。
类别：0 银钢 / 1 黑铁 / 2 鎏金 / 3 角 / 4 骨 / 5 古铜铆钉"""
import sys; sys.path.insert(0, '/tmp/helm'); from common import *
from scipy import ndimage as ndi
s = cv2.imread('/tmp/helm/src_albedo_srgb.png')[..., ::-1].astype(np.float32) / 255
P = rd('pos', False); nrm = rd('nrm', False); idm = rd('srcid', False)[..., 0]
valid = np.linalg.norm(nrm, axis=2) > 0.5
X, Y, Z = P[..., 0], P[..., 1], P[..., 2]
H, W = valid.shape
mid = np.full((H, W), -1, np.int32)


def lab_of(name, m):
    cen = np.load(f'/tmp/helm/cen_{name}.npy')
    out = np.full((H, W), -1, np.int32)
    out[m] = ((s[m][:, None, :] - cen[None]) ** 2).sum(2).argmin(1)
    return out, cen


# 骑士：灰 → 钢，米黄 → 鎏金，最暗 → 黑铁（缝隙、眼缝里面）
kn = (abs(idm - 0.2) < 0.05) & valid
kl, kc = lab_of('knight', kn)
order = np.argsort(kc.sum(1))          # 从暗到亮
kmap = np.zeros(len(kc), np.int32)
kmap[order[0]] = 1; kmap[order[1]] = 0; kmap[order[2]] = 0; kmap[order[3]] = 2
mid[kn] = kmap[kl[kn]]

# 兽人：暗的两类 → 黑铁；中间两类 → 角（伸出盔外的）或骨（脸前面的）；最亮 → 骨
orc = (abs(idm - 0.5) < 0.05) & valid
ol, oc = lab_of('orc', orc)
order = np.argsort(oc.sum(1))
dark = np.isin(ol, order[:2]); midc = np.isin(ol, order[2:4]); light = ol == order[4]
mid[orc & dark] = 1
face = (X > 0.06) & (abs(Y) < 0.36) & (Z < 0.22) & (Z > -0.42)
horn_zone = abs(Y) > 0.30
mid[orc & midc & horn_zone] = 3
mid[orc & midc & ~horn_zone & face] = 4
mid[orc & midc & ~horn_zone & ~face] = 1
mid[orc & light & face] = 4
mid[orc & light & ~face] = 5           # 脸外面的亮点是铆钉
mid[orc & (abs(Y) > 0.40)] = 3            # 盔壳外面的只有角

# 狮子：全鎏金
mid[(abs(idm - 0.8) < 0.05) & valid] = 2

# 去噪：5×5 众数
oh = np.stack([cv2.blur((mid == k).astype(np.float32), (5, 5)) for k in range(6)], -1)
mid2 = np.where(valid, oh.argmax(-1), -1)
# 角：整块（连通区域）统一，免得角纹暗处被分成铁
hl, nh = ndi.label(mid2 == 3)
area = ndi.sum(np.ones_like(valid), hl, range(1, nh + 1))
small = np.zeros(nh + 1, bool); small[1:] = area < 300
mid2[small[hl]] = 1
# 骨区里的小亮块（盔面上的铆钉）→ 古铜
bl, nb = ndi.label(mid2 == 4)
ba = ndi.sum(np.ones_like(valid), bl, range(1, nb + 1))
sb = np.zeros(nb + 1, bool); sb[1:] = ba < 400
mid2[sb[bl]] = 5
np.save('/tmp/helm/matid_final.npy', mid2)
pal = np.array([[200, 200, 210], [40, 40, 45], [230, 180, 60], [150, 110, 70], [235, 225, 200], [180, 90, 40]], np.uint8)
img = np.zeros((H, W, 3), np.uint8); img[valid] = pal[mid2[valid]]
cv2.imwrite('/tmp/helm/matid2.png', img[..., ::-1])
print('frac', [round(float((mid2 == k).sum()) / valid.sum(), 3) for k in range(6)])

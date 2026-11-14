"""双面战盔 v2：按分区重画 PBR（UV0，2048）。保留 P2 原图里的雕花、角纹、骷髅明暗（亮度高通 / 归一化明暗），
金属的颜色、金属度、光滑度全部重做。输出 out/helm_albedo|metal|normal|ao.png。"""
import sys, os; sys.path.insert(0, '/tmp/helm'); from common import *
N = 2048
src = cv2.imread('/tmp/helm/src_albedo_srgb.png')[..., ::-1].astype(np.float32) / 255
P = rd('pos', False); nrm = rd('nrm', False); valid = np.linalg.norm(nrm, axis=2) > 0.5
ao = rd('ao', False)[..., 0]; pt = rd('point', False)[..., 0]
mid = np.load('/tmp/helm/matid_final.npy')


def vnoise(p, freq, seed):
    g = np.random.default_rng(seed).random((64, 64, 64)).astype(np.float32)
    q = p * freq; i = np.floor(q).astype(np.int64); f = q - i; f = f * f * (3 - 2 * f)
    out = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[..., 0] if dx else 1 - f[..., 0]) * (f[..., 1] if dy else 1 - f[..., 1]) * (f[..., 2] if dz else 1 - f[..., 2])
                out = out + w * g[(i[..., 0] + dx) % 64, (i[..., 1] + dy) % 64, (i[..., 2] + dz) % 64]
    return out


def fbm(p, f0, oct, seed):
    s = 0; a = 1; t = 0
    for o in range(oct): s = s + a * vnoise(p, f0 * 2 ** o, seed + o); t += a; a *= 0.5
    return s / t


Pv = P[valid]
def field(fn):
    o = np.zeros((N, N), np.float32); o[valid] = fn(Pv); return o
n_low = field(lambda p: fbm(p, 4, 3, 1))
n_mid = field(lambda p: fbm(p, 18, 3, 11))
n_fine = field(lambda p: fbm(p, 80, 2, 21))
brush = field(lambda p: fbm(p * np.array([1, 1, 60], np.float32), 3, 3, 31))
cav = np.clip(1 - ao, 0, 1)
edge = np.clip((pt - 0.585) / 0.03, 0, 1)
conc = np.clip((0.5 - pt) / 0.04, 0, 1)
lum = src.mean(-1)
def hp(x, s): return x - cv2.GaussianBlur(x, (0, 0), s)
detail = hp(lum, 3)                                     # 雕花、角纹、铆钉轮廓
shade = np.clip(cv2.GaussianBlur(lum, (0, 0), 1.2) / np.maximum(cv2.GaussianBlur(lum, (0, 0), 24), 0.05), 0.3, 1.6)   # 原图局部明暗

M = {k: (mid == k) & valid for k in range(6)}
alb = np.zeros((N, N, 3), np.float32); met = np.zeros((N, N), np.float32); smo = np.zeros((N, N), np.float32); hgt = np.zeros((N, N), np.float32)
one = np.ones((N, N), np.float32)
def put(m, c, me, sm, h):
    alb[m] = c[m] if c.ndim == 3 else c; met[m] = me[m] if np.ndim(me) == 2 else me
    smo[m] = sm[m] if np.ndim(sm) == 2 else sm; hgt[m] = h[m] if np.ndim(h) == 2 else h
def C(r, g, b): return np.array([r, g, b], np.float32)[None, None]
def E(x): return x[..., None]

# 0 银钢：缎面钢、竖向细拉丝、凹处微暗
oxid = np.clip((n_low - 0.45) * 2.5, 0, 1)
steel = C(0.46, 0.465, 0.47) * E(0.85 + 0.2 * n_low) * E(1 - 0.45 * cav) * E(1 - 0.18 * oxid) + C(0.05, 0.04, 0.03) * E(0.25 * cav)
put(M[0], steel, one, 0.46 + 0.06 * n_mid - 0.05 * brush - 0.14 * oxid - 0.2 * cav, 0.12 * brush + 0.15 * n_low)
# 1 黑铁：发蓝熟铁、锻打颗粒，凸边磨出钢色
iron = C(0.05, 0.052, 0.058) * E(0.8 + 0.5 * n_mid)
iron = iron * E(1 - 0.55 * edge) + C(0.26, 0.26, 0.27) * E(0.55 * edge)
iron = iron * E(1 - 0.4 * cav)
put(M[1], iron, 0.85 * one, 0.26 + 0.1 * n_mid + 0.14 * edge - 0.12 * cav, 0.6 * n_mid + 0.25 * n_fine)
# 2 鎏金：暖金，雕花刻槽沉成古铜（原图里的雕花），凸边擦亮；狮子靠原图明暗撑形
eng = np.clip(-detail * 8, 0, 1)                        # 原图里比周围暗的细线 = 刻槽
gold = C(0.66, 0.47, 0.21) * E(0.85 + 0.2 * n_low)
gold = gold * E(1 - 0.6 * np.maximum(cav, eng)) + C(0.22, 0.12, 0.04) * E(0.6 * np.maximum(cav, eng))
gold = gold * E(1 - 0.3 * edge) + C(0.98, 0.82, 0.48) * E(0.3 * edge)
lion = P[..., 2] > 0.5
gold = np.where(E(lion), gold * E(0.35 + 0.65 * shade), gold)
put(M[2], gold, one, np.where(P[..., 2] > 0.5, 0.34, 0.42) + 0.1 * edge - 0.22 * np.maximum(cav, eng) + 0.05 * n_mid, -1.2 * eng + 0.1 * n_mid)
# 3 角：原图角纹 + 象牙到深褐渐变（越往角尖越深）
hv = np.clip(0.5 + 2.5 * detail, 0, 1)
tipd = np.clip((np.abs(P[..., 1]) - 0.35) / 0.3, 0, 1)
horn = (C(0.17, 0.12, 0.075) * E(1 - tipd) + C(0.06, 0.045, 0.03) * E(tipd)) * E(0.45 + 0.8 * hv) * E(np.clip(shade, 0.5, 1.1))
put(M[3], horn, 0 * one, 0.28 + 0.1 * hv - 0.12 * cav, 2.5 * detail)
# 4 骨：陈旧骨色，原图明暗撑形，凹缝深褐
bone = C(0.24, 0.205, 0.15) * E(0.85 + 0.25 * n_low) * E(np.clip(shade, 0.55, 1.15))
bone = bone * E(1 - 0.6 * conc) + C(0.07, 0.045, 0.025) * E(0.6 * conc)
put(M[4], bone, 0 * one, 0.22 - 0.1 * conc + 0.04 * n_mid, 0.4 * n_mid + 1.2 * detail)
# 5 古铜铆钉
bro = C(0.42, 0.28, 0.14) * E(1 - 0.5 * cav)
put(M[5], bro, one, 0.34 + 0.14 * edge - 0.15 * cav, 0 * one)

# 内壁（法线朝盔内）：透过眼窝、嘴、底口看进去的地方，做成哑光暗铁，不反光
ctr = np.array([0, 0, -0.02], np.float32); dv = P - ctr; dv[..., 2] *= 0.6
inner = valid & ((nrm * dv).sum(-1) < -0.02 * np.linalg.norm(dv, axis=-1)) & (P[..., 2] < 0.5) & np.isin(mid, [0, 1, 5]) & (np.abs(P[..., 1]) < 0.36)
# 深凹（眼窝里、嘴里）也不该映出环境
deep = valid & np.isin(mid, [1]) & (ao < 0.3)
smo[deep] = np.minimum(smo[deep], 0.2); alb[deep] *= 0.5
alb[inner] = [0.018, 0.016, 0.015]; met[inner] = 0.3; smo[inner] = 0.15
hole = valid & (mid == 1) & (ao < 0.35) & (P[..., 0] > 0.05) & (P[..., 1] > 0.02) & (P[..., 2] < 0.2)
alb[hole] = [0.012, 0.01, 0.009]; met[hole] = 0; smo[hole] = 0.1
for c in range(3): alb[..., c] = np.where(valid, cv2.GaussianBlur(alb[..., c], (0, 0), 0.7), 0)
met = np.where(valid, cv2.GaussianBlur(met, (0, 0), 0.7), 0)
smo = np.clip(np.where(valid, cv2.GaussianBlur(smo, (0, 0), 0.7), 0), 0.05, 0.95)
er = cv2.erode(valid.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool)
h = cv2.GaussianBlur(hgt, (0, 0), 0.8)
gx = cv2.Sobel(h, cv2.CV_32F, 1, 0, ksize=3) / 8; gy = -cv2.Sobel(h, cv2.CV_32F, 0, 1, ksize=3) / 8
k = 1.6
nx = -gx * k; ny = -gy * k; L = np.sqrt(nx * nx + ny * ny + 1)
nmap = np.stack([nx / L, ny / L, 1 / L], -1); nmap[~er] = [0, 0, 1]
def dilate(img, m, it=12):
    img = img.copy(); m = m.copy()
    for _ in range(it):
        acc = cv2.blur(img * m[..., None] if img.ndim == 3 else img * m, (3, 3)); w = cv2.blur(m.astype(np.float32), (3, 3))
        new = (w > 0) & ~m
        if img.ndim == 3: img[new] = acc[new] / w[new][:, None]
        else: img[new] = acc[new] / w[new]
        m = m | new
    return img
alb = dilate(alb, valid); met = dilate(met, valid); smo = dilate(smo, valid)
aoc = np.where(mid == 4, np.maximum(ao, 0.6), ao)
occ = dilate(np.clip(0.25 + 0.75 * aoc, 0, 1), valid)
def srgb(x): return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(np.clip(x, 0, 1), 1 / 2.4) - 0.055)
O = '/tmp/helm/out'; os.makedirs(O, exist_ok=True)
cv2.imwrite(f'{O}/helm_albedo.png', (np.clip(srgb(alb), 0, 1) * 255 + 0.5).astype(np.uint8)[..., ::-1])
cv2.imwrite(f'{O}/helm_metal.png', (np.dstack([met, met, met, smo]) * 255 + 0.5).astype(np.uint8))
cv2.imwrite(f'{O}/helm_normal.png', ((nmap * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)[..., ::-1])
cv2.imwrite(f'{O}/helm_ao.png', (occ * 255 + 0.5).astype(np.uint8))
print('ok')

# Blender 自带的 aud（内含 ffmpeg）：解码任意格式 → 量响度 → 统一到目标 RMS → 首尾加淡入淡出 → 写成 OGG Vorbis
import aud, numpy as np, sys, json
jobs = json.load(open('/tmp/sp2/music1117/jobs.json')); TARGET = -20.0; rep = []
for job in jobs:
    src, dst, t0, t1 = job[:4]; fo_s = job[4] if len(job) > 4 else 2.5; TARGET = job[5] if len(job) > 5 else -20.0
    s = aud.Sound(src); sr, ch = s.specs; sr = int(sr); ch = int(ch); x = np.array(s.data(), np.float32)
    if t1 > 0: x = x[int(t0 * sr):int(t1 * sr)]
    elif t0 > 0: x = x[int(t0 * sr):]
    m = x.mean(1); thr = 10 ** (-50 / 20); nz = np.where(np.abs(m) > thr)[0]; x = x[max(0, nz[0] - int(0.05 * sr)):min(len(x), nz[-1] + int(0.3 * sr))]      # 去掉首尾的静音
    rms = 20 * np.log10(np.sqrt((x.mean(1) ** 2).mean()) + 1e-9); g = 10 ** ((TARGET - rms) / 20); y = x * g
    pk = np.abs(y).max()
    if pk > 0.98: y = np.tanh(y / 0.98 * 1.0) * 0.98 if pk > 1.4 else y * (0.98 / pk)       # 峰值太高：轻微压一下 / 整体让一点
    n = len(y); fi = int(min(0.4, fo_s * 0.1) * sr); fo = int(fo_s * sr); y[:fi] *= np.linspace(0, 1, fi)[:, None]; y[-fo:] *= np.linspace(1, 0, fo)[:, None]
    out = aud.Sound.buffer(np.ascontiguousarray(y, np.float32), sr)
    out.write(dst, sr, ch, aud.FORMAT_FLOAT32, aud.CONTAINER_OGG, aud.CODEC_VORBIS, 176000)
    rms2 = 20 * np.log10(np.sqrt((y.mean(1) ** 2).mean()) + 1e-9)
    # 粗看一下曲子的性格：每秒的响度起伏、低频 / 高频能量比
    sec = np.array([np.sqrt((y[i:i + sr // 10].mean(1) ** 2).mean()) for i in range(0, max(1, n - sr // 10), sr // 10)]); db = 20 * np.log10(sec + 1e-9)
    rep.append(f'{dst.split("/")[-1]:22s} {n / sr:6.1f}s  原 RMS {rms:6.1f} → {rms2:6.1f} dB  峰值 {20 * np.log10(np.abs(y).max()):5.1f}  每秒响度 min {db.min():5.1f} / 中位 {np.median(db):5.1f} / max {db.max():5.1f}  前10秒 {db[:10].mean():5.1f}')
open('/tmp/sp2/music1117/report.txt', 'w').write('\n'.join(rep))

"""开战的号角（11-17：素材库里没有授权清楚的战争号角 → 程序合成，版权干净）。
用 Blender 自带的 numpy + aud 跑：Blender -b -P Tools/make_war_horn.py   → Assets/_WarTable/Audio/UI/war_horn.ogg
声音怎么来的：
  · 加法合成（把一个音拆成基频和它的整数倍泛音，各自一个正弦波再加起来）：铜管的特点是“吹得越响越亮”，所以高次泛音的份量跟着音量包络走；
  · 三支号角各差几音分（1 音分 = 半音的 1/100）叠在一起，起音时从低 60 音分滑上来，稳住以后带一点 4.6 Hz 的颤音；
  · 两声：先一声低的（D3），接一声高五度的（A3），像“呜——呜——”的集结号；
  · 气声：一层按包络起伏的带通噪声；
  · 混响：和一段指数衰减的噪声做卷积（相当于在大空间里吹），干声 : 湿声约 7 : 3。"""
import numpy as np, aud
SR = 48000
def env_adsr(n, a, r, swell=0.25):
    t = np.arange(n) / SR; T = n / SR
    e = np.clip(t / a, 0, 1) ** 1.6 * np.clip((T - t) / r, 0, 1) ** 1.3
    return e * (1 - swell + swell * np.clip(t / (T * 0.6), 0, 1))
def horn(f0, dur, seed):
    n = int(dur * SR); t = np.arange(n) / SR; rng = np.random.default_rng(seed); out = np.zeros(n)
    e = env_adsr(n, 0.22, 0.55)
    for cents in (-7, 0, 6):
        scoop = -60 * np.exp(-t / 0.09)                                     # 起音从低处滑上来
        vib = 6 * np.sin(2 * np.pi * 4.6 * t + rng.random() * 6) * np.clip((t - 0.7) / 0.6, 0, 1)
        f = f0 * 2 ** ((cents + scoop + vib) / 1200); ph = 2 * np.pi * np.cumsum(f) / SR
        v = np.zeros(n)
        for k in range(1, 26):
            if f0 * k > 9000: break
            p = 1.9 - 1.0 * e                                               # 越响，高次泛音衰减得越慢（越亮）
            form = 1 + 1.6 * np.exp(-((f0 * k - 520) / 300) ** 2) + 0.7 * np.exp(-((f0 * k - 1250) / 420) ** 2)   # 号口的共鸣峰
            v += np.sin(k * ph + rng.random() * 6.28) * form / k ** p
        out += v * e * (0.9 + 0.2 * rng.random())
    noise = rng.normal(0, 1, n); spec = np.fft.rfft(noise); fr = np.fft.rfftfreq(n, 1 / SR)
    spec *= np.exp(-((fr - 900) / 700) ** 2); breath = np.fft.irfft(spec, n); breath /= np.abs(breath).max() + 1e-9
    return out / np.abs(out).max() + breath * e * 0.035
a = horn(146.83, 1.9, 1); b = horn(220.0, 2.5, 2)
n = int(4.3 * SR) + int(3.0 * SR); dry = np.zeros(n); dry[:len(a)] += a * 0.9; o = int(1.55 * SR); dry[o:o + len(b)] += b
rng = np.random.default_rng(9); L = int(2.6 * SR); t = np.arange(L) / SR
def ir(seed):
    r = np.random.default_rng(seed).normal(0, 1, L) * np.exp(-t / 0.42); s = np.fft.rfft(r); fr = np.fft.rfftfreq(L, 1 / SR); s *= 1 / (1 + (fr / 3200) ** 2); return np.fft.irfft(s, L)
def conv(x, h):
    N = 1 << int(np.ceil(np.log2(len(x) + len(h)))); return np.fft.irfft(np.fft.rfft(x, N) * np.fft.rfft(h, N), N)[:len(x)]
wl, wr = conv(dry, ir(11)), conv(dry, ir(12)); wl /= np.abs(wl).max(); wr /= np.abs(wr).max()
st = np.stack([dry * 0.7 + wl * 0.32, dry * 0.7 + wr * 0.32], 1)
st = st[:int(5.6 * SR)]; fo = int(1.2 * SR); st[-fo:] *= np.linspace(1, 0, fo)[:, None]
rms = np.sqrt((st.mean(1) ** 2).mean()); st *= 10 ** (-19 / 20) / rms; pk = np.abs(st).max()
if pk > 0.97: st *= 0.97 / pk
aud.Sound.buffer(np.ascontiguousarray(st, np.float32), SR).write('/Users/ld/Documents/ChatGPT/WarTable/Assets/_WarTable/Audio/UI/war_horn.ogg', SR, 2, aud.FORMAT_FLOAT32, aud.CONTAINER_OGG, aud.CODEC_VORBIS, 160000)
print('horn ok', round(len(st) / SR, 2), 's rms', round(20 * np.log10(np.sqrt((st.mean(1) ** 2).mean())), 1), 'peak', round(20 * np.log10(np.abs(st).max()), 1))

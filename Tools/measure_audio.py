"""量游戏里每个音频文件的响度（RMS / 峰值 dBFS），用来配平音量。python3 Tools/measure_audio.py"""
import glob, os, subprocess, wave, tempfile
import numpy as np
R='/Users/ld/Documents/ChatGPT/WarTable/Assets/_WarTable/Audio/'
tmp=tempfile.mkdtemp()
for f in sorted(glob.glob(R+'*/*.mp3')):
    w=os.path.join(tmp,'x.wav')
    subprocess.run(['afconvert','-f','WAVE','-d','LEI16@22050','-c','1',f,w],check=True,capture_output=True)
    ww=wave.open(w); sr=ww.getframerate(); x=np.frombuffer(ww.readframes(ww.getnframes()),np.int16).astype(float)/32768
    if len(x)==0: continue
    # 有声部分的 RMS（去掉首尾静音，短音效才不会被静音拉低）
    env=np.sqrt(np.convolve(x**2,np.ones(256)/256,'same'))
    act=env>env.max()*0.1
    rms=np.sqrt((x[act]**2).mean()) if act.any() else 1e-9
    pk=np.abs(x).max()
    print(f"{os.path.relpath(f,R):28s} {len(x)/sr:6.2f}s  RMS {20*np.log10(rms+1e-9):6.1f}  峰值 {20*np.log10(pk+1e-9):6.1f}")

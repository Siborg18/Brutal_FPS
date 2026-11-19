"""Cuts the game clips in Assets/Sounds/Weapons out of the raw recordings in Assets/Sounds: finds each click, trims
around it, high-passes at 110 Hz (rumble and hum), fades in/out, writes 16-bit 48 kHz stereo WAVs.
Run: python Tools/audio/cut_sounds.py   (needs numpy and scipy). If a clip's click moves, update the *Click constants in Weapon.cs."""
import numpy as np, sys, json
from scipy.signal import butter, sosfiltfilt
SR=48000
import os
ROOT=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
SRC=os.path.join(ROOT, "Assets", "Sounds") + os.sep
OUT=os.path.join(SRC, "Weapons")
os.makedirs(OUT, exist_ok=True)
def load(f):
    d=open(SRC+f,'rb').read(); pos=12
    while pos<len(d):
        cid=d[pos:pos+4]; sz=int.from_bytes(d[pos+4:pos+8],'little')
        if cid==b'data': raw=d[pos+8:pos+8+sz]; break
        pos+=8+sz+(sz&1)
    a=np.frombuffer(raw,dtype=np.uint8).reshape(-1,3)
    v=(a[:,0].astype(np.int32)|(a[:,1].astype(np.int32)<<8)|(a[:,2].astype(np.int32)<<16))
    v=np.where(v>=1<<23,v-(1<<24),v)/float(1<<23)
    return v.reshape(-1,2)
def onset(x, around, search=0.06):
    # first sample near `around` where the level jumps well above the 50 ms before it
    m=np.abs(x).max(1)
    a=int((around-search)*SR); b=int((around+search)*SR)
    noise=np.sqrt((x[a-int(0.05*SR):a]**2).mean())
    idx=np.where(m[a:b]>noise*8)[0]
    return (a+idx[0])/SR if len(idx) else around
def save(name,x):
    y=np.clip(np.round(x*32767),-32768,32767).astype('<i2')
    data=y.tobytes()
    hdr=b'RIFF'+(36+len(data)).to_bytes(4,'little')+b'WAVEfmt '+(16).to_bytes(4,'little')
    hdr+=(1).to_bytes(2,'little')+(2).to_bytes(2,'little')+SR.to_bytes(4,'little')+(SR*4).to_bytes(4,'little')+(4).to_bytes(2,'little')+(16).to_bytes(2,'little')
    hdr+=b'data'+len(data).to_bytes(4,'little')
    open(os.path.join(OUT, name),"wb").write(hdr+data)
hp=butter(4,110,'highpass',fs=SR,output='sos')
info={}
def make(out, src, hit_guess, pre, post, fade_out, gain_db=0):
    x=load(src)
    hit=onset(x,hit_guess)
    a=int((hit-pre)*SR); b=int((hit+post)*SR)
    y=sosfiltfilt(hp,x[a-SR//10:b+SR//10],axis=0)[SR//10:SR//10+(b-a)]
    n=len(y); t=np.arange(n)/SR
    fi=np.clip(t/0.003,0,1); fo=np.clip((n/SR-t)/fade_out,0,1)**2
    y=y*(fi*fo)[:,None]
    y*=10**(gain_db/20)
    pk=np.abs(y).max()
    if pk>0.97: y*=0.97/pk
    save(out,y)
    info[out]=dict(source=src, cut=[round(hit-pre,4), round(hit+post,4)], hit=round(pre,4), length=round(n/SR,3), peak=round(float(np.abs(y).max()),3))
make("pistol_shot.wav","Gunshot.wav",1.33,0.004,0.85,0.35)
make("pistol_mag_out.wav","UnloadClip.wav",4.80,0.10,0.45,0.12,gain_db=3)
make("pistol_mag_in.wav","LoadClip.wav",2.58,0.45,0.30,0.12)
print(json.dumps(info,indent=1))

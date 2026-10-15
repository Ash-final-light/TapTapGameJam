"""WASAPI playback-loopback metrics; no microphone and no raw recording saved."""
from pathlib import Path
import json
import sys
import numpy as np
import soundcard as sc

rate = 48000
speaker = sc.default_speaker()
loopback = sc.get_microphone(id=speaker.id, include_loopback=True)
samples = loopback.record(samplerate=rate, numframes=int(rate * float(sys.argv[2])), channels=[0, 1])
mono = samples.mean(axis=1)
n = 4800
t = np.arange(n) / rate
window = np.hanning(n)
reference = window * np.exp(-2j * np.pi * 421 * t)
levels = [2 * abs(np.dot(mono[i:i + n], reference)) / window.sum() for i in range(0, len(mono) - n, n // 2)]
result = {
    "method": "Windows default playback device WASAPI loopback; 421 Hz known input reference",
    "seconds": len(mono) / rate,
    "rms": float(np.sqrt(np.mean(mono * mono))),
    "peak": float(np.max(np.abs(mono))),
    "reference_421hz_median": float(np.median(levels)),
}
Path(sys.argv[1]).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(result))

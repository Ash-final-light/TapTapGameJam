"""Generate known, original signals for local custom-song player validation."""
from pathlib import Path
import sys
import wave
import numpy as np

root = Path(sys.argv[1])
root.mkdir(parents=True, exist_ok=True)
rate = 22050


def write(name, seconds, frequencies):
    t = np.arange(rate * seconds) / rate
    band = (t * 2).astype(int) % len(frequencies)
    frequency = np.array(frequencies)[band]
    envelope = np.exp(-(t % .5) * 14)
    samples = .4 * np.sin(2 * np.pi * frequency * t) * envelope
    # Quiet reference tone makes output and pause checks unambiguous.
    samples += .045 * np.sin(2 * np.pi * 421 * t)
    path = root / name
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as audio:
        audio.setnchannels(1)
        audio.setsampwidth(2)
        audio.setframerate(rate)
        audio.writeframes((samples * 32767).astype("<i2").tobytes())


write("auto/low.wav", 12, [100])
write("auto/mid.wav", 12, [800])
write("auto/high.wav", 12, [4500])
write("auto/mixed_short.wav", 12, [100, 800, 4500])
write("节拍测试 # 共鸣舞台.wav", 90, [100, 800, 4500])

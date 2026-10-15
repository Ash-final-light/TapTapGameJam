"""Windows validation only: WASAPI playback loopback and a known 440 Hz input.

Requires numpy and soundcard in the validation Python environment, not the game.
Use make-input PATH, record PATH SECONDS, or compare ON.wav OFF.wav RESTORED.wav REPORT.json.
Record only while the isolated game is playing and other desktop audio is silent.
"""
import json
import sys
import wave
from pathlib import Path

import numpy as np

RATE = 48000


def write_wav(path, samples):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    if samples.ndim == 1:
        samples = samples[:, None]
    with wave.open(str(path), "wb") as output:
        output.setnchannels(samples.shape[1])
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes((np.clip(samples, -1, 1) * 32767).astype("<i2").tobytes())


def analyze(path):
    with wave.open(str(path), "rb") as audio:
        assert audio.getframerate() == RATE and audio.getsampwidth() == 2
        data = np.frombuffer(audio.readframes(audio.getnframes()), dtype="<i2").astype(float) / 32767
        samples = data.reshape(-1, audio.getnchannels()).mean(axis=1)
    window = np.hanning(960)
    time = np.arange(960) / RATE
    projections = [window * np.exp(-2j * np.pi * frequency * time) for frequency in (440, 2200)]
    amplitudes = np.array([[2 * abs(np.dot(samples[start:start + 960], p)) / window.sum()
                            for p in projections] for start in range(0, len(samples) - 960, 480)])
    click = amplitudes[:, 1]
    above = np.flatnonzero(click > max(.001, click.max() * .25))
    groups = np.split(above, np.flatnonzero(np.diff(above) > 1) + 1) if len(above) else []
    peaks = [(group[np.argmax(click[group])] * .01 + .01) for group in groups if len(group)]
    return {
        "file": Path(path).name,
        "seconds": len(samples) / RATE,
        "rms": float(np.sqrt(np.mean(samples * samples))),
        "peak": float(np.max(np.abs(samples))),
        "music_440hz_median_amplitude": float(np.median(amplitudes[:, 0])),
        "click_2200hz_peak_amplitude": float(click.max()),
        "click_peak_seconds": peaks,
        "median_click_interval_seconds": float(np.median(np.diff(peaks))) if len(peaks) > 1 else None,
    }


if sys.argv[1] == "make-input":
    time = np.arange(RATE * 180) / RATE
    write_wav(sys.argv[2], .6 * np.sin(2 * np.pi * 440 * time))
elif sys.argv[1] == "record":
    import soundcard as sc
    speaker = sc.default_speaker()
    loopback = sc.get_microphone(id=speaker.id, include_loopback=True)
    samples = loopback.record(samplerate=RATE, numframes=round(RATE * float(sys.argv[3])), channels=[0, 1])
    write_wav(sys.argv[2], samples)
    print(json.dumps(analyze(sys.argv[2]), ensure_ascii=False))
elif sys.argv[1] == "compare":
    on, off, restored = [analyze(path) for path in sys.argv[2:5]]
    ratio = on["music_440hz_median_amplitude"] / off["music_440hz_median_amplitude"]
    restored_ratio = restored["music_440hz_median_amplitude"] / off["music_440hz_median_amplitude"]
    report = {
        "method": "Windows default playback device WASAPI loopback, 48 kHz stereo; known 440 Hz input",
        "on": on, "off": off, "restored": restored,
        "music_gain_on_over_off": ratio,
        "music_gain_restored_over_off": restored_ratio,
        "passed": bool(.27 < ratio < .33 and len(on["click_peak_seconds"]) >= 6
                       and abs(on["median_click_interval_seconds"] - .5) < .025
                       and on["click_2200hz_peak_amplitude"] > 10 * off["click_2200hz_peak_amplitude"]
                       and restored["click_2200hz_peak_amplitude"] < .001 and .9 < restored_ratio < 1.1),
    }
    Path(sys.argv[5]).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    if not report["passed"]:
        sys.exit(1)
else:
    raise ValueError("Unknown operation")

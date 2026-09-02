"""
Эталон для сверки с C#: тот же алгоритм, но на Python.

Печатает в том же формате, что MlCheck, чтобы выдачи сравнивались построчно.
Расхождение здесь означает, что C# готовит звук иначе (чаще всего — ресемплинг)
и в приложении пользователь увидит не тех артистов.
"""
import argparse
import struct
import subprocess
import sys
from pathlib import Path

import numpy as np
import onnxruntime as ort

SAMPLE_RATE = 24000
WINDOW_SAMPLES = 10 * SAMPLE_RATE
MAX_WINDOWS = 6          # столько же, сколько берёт C#
TOP_TRACKS_PER_ARTIST = 3


def decode(path):
    """Через ffmpeg — эталонный ресемплинг, с которым сверяется C#."""
    raw = subprocess.run(
        ["ffmpeg", "-nostdin", "-v", "error", "-i", str(path),
         "-ac", "1", "-ar", str(SAMPLE_RATE), "-f", "f32le", "-"],
        capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32)


def slice_windows(samples):
    if samples.size < 3 * SAMPLE_RATE:
        return None
    if samples.size <= WINDOW_SAMPLES:
        return np.pad(samples, (0, WINDOW_SAMPLES - samples.size))[None, :]

    count = min(MAX_WINDOWS, samples.size // WINDOW_SAMPLES)
    starts = np.linspace(0, samples.size - WINDOW_SAMPLES, count).astype(int)
    return np.stack([samples[s:s + WINDOW_SAMPLES] for s in starts])


def load_index(path):
    with open(path, "rb") as handle:
        n, d = struct.unpack("<ii", handle.read(8))
        center = np.frombuffer(handle.read(d * 4), dtype=np.float32)
        vectors = np.frombuffer(handle.read(n * d * 4), dtype=np.float32).reshape(n, d)
        (artist_count,) = struct.unpack("<i", handle.read(4))
        names = []
        for _ in range(artist_count):
            (length,) = struct.unpack("<i", handle.read(4))
            names.append(handle.read(length).decode("utf-8"))
        ids = np.frombuffer(handle.read(n * 4), dtype=np.int32)
    return center, vectors, names, ids


def to_percent(similarity, low=0.05, high=0.65):
    return int(round(float(np.clip((similarity - low) / (high - low), 0, 1)) * 100))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("model")
    parser.add_argument("index")
    parser.add_argument("audio", nargs="+")
    args = parser.parse_args()

    session = ort.InferenceSession(args.model, providers=["CPUExecutionProvider"])
    center, vectors, names, ids = load_index(args.index)

    by_artist = {a: np.where(ids == a)[0] for a in range(len(names))}

    for path in args.audio:
        samples = decode(path)
        windows = slice_windows(samples)
        if windows is None:
            print(f"ERROR\t{Path(path).name}\tслишком короткий")
            continue

        vector = session.run(["embedding"], {"waveform": windows})[0].mean(axis=0)
        vector = vector - center
        vector = vector / (np.linalg.norm(vector) + 1e-9)

        similarity = vectors @ vector
        scores = []
        for artist, indices in by_artist.items():
            values = similarity[indices]
            if values.size > TOP_TRACKS_PER_ARTIST:
                values = np.partition(values, -TOP_TRACKS_PER_ARTIST)[-TOP_TRACKS_PER_ARTIST:]
            scores.append((names[artist], float(values.mean())))

        scores.sort(key=lambda kv: -kv[1])

        print(f"FILE\t{Path(path).name}")
        print(f"SAMPLES\t{samples.size}")
        for name, score in scores[:5]:
            print(f"MATCH\t{name}\t{score:.6f}\t{to_percent(score)}")


if __name__ == "__main__":
    main()

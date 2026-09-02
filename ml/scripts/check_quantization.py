"""
Портит ли int8-квантизация ранжирование по-настоящему?

Расхождение в самих числах (0.48) пугает, но нас волнует только одно: меняется ли
порядок артистов в выдаче. Проверяем честно — считаем эмбеддинги подвыборки самой
квантованной моделью (а не PyTorch), строим на них галерею и меряем ту же метрику.
Так сравниваются два самостоятельных пространства, а не одно с испорченной копией.
"""
import argparse
import csv
from collections import defaultdict
from pathlib import Path

import numpy as np
import soundfile as sf

WINDOW_SAMPLES = 10 * 24000
WINDOWS_PER_TRACK = 5
TOP_K = 3
GALLERY_CAP = 40


def windows_for(path):
    audio, _ = sf.read(path, dtype="float32", always_2d=False)
    if audio.ndim > 1:
        audio = audio.mean(axis=1)
    if audio.size < WINDOW_SAMPLES:
        return None
    count = min(WINDOWS_PER_TRACK, audio.size // WINDOW_SAMPLES)
    starts = np.linspace(0, audio.size - WINDOW_SAMPLES, count).astype(int)
    return np.stack([audio[s:s + WINDOW_SAMPLES] for s in starts])


def normalize(matrix):
    centered = matrix - matrix.mean(axis=0, keepdims=True)
    return centered / (np.linalg.norm(centered, axis=1, keepdims=True) + 1e-9)


def measure(embeddings, artist_ids, album_ids, n_artists):
    embeddings = normalize(embeddings)
    similarity = embeddings @ embeddings.T
    by_artist = {a: np.where(artist_ids == a)[0] for a in range(n_artists)}
    rng = np.random.default_rng(0)

    top1 = top5 = counted = 0
    for i in range(len(embeddings)):
        scores = np.full(n_artists, -np.inf, dtype=np.float32)
        for artist, indices in by_artist.items():
            pool = indices[album_ids[indices] != album_ids[i]]
            if pool.size == 0:
                continue
            if pool.size > GALLERY_CAP:
                pool = rng.choice(pool, GALLERY_CAP, replace=False)
            values = similarity[i, pool]
            if values.size > TOP_K:
                values = np.partition(values, -TOP_K)[-TOP_K:]
            scores[artist] = values.mean()

        if not np.isfinite(scores).any():
            continue
        ranking = np.argsort(-scores)
        top1 += ranking[0] == artist_ids[i]
        top5 += artist_ids[i] in ranking[:5]
        counted += 1

    return top1 / counted, top5 / counted, counted


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", default="../MessedUpSearchA/Assets/Models/mert_layer5_int8.onnx")
    parser.add_argument("--per-artist", type=int, default=14,
                        help="сколько треков на артиста брать в подвыборку")
    args = parser.parse_args()

    import onnxruntime as ort

    base = Path(__file__).resolve().parent.parent
    with (base / "data/manifest24.csv").open(encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))

    # Стратифицированная подвыборка: поровну от каждого артиста, иначе крупные
    # артисты забьют выборку и метрика будет не про то.
    by_artist = defaultdict(list)
    for row in rows:
        by_artist[row["artist"]].append(row)

    rng = np.random.default_rng(0)
    subset = []
    for artist, tracks in sorted(by_artist.items()):
        take = min(args.per_artist, len(tracks))
        subset.extend(rng.choice(tracks, take, replace=False).tolist())

    print(f"подвыборка: {len(subset)} треков, {len(by_artist)} артистов")

    # Эталон — те же треки, но из уже посчитанных PyTorch-эмбеддингов
    data = np.load(base / "data/embeddings_mert.npz", allow_pickle=True)
    key_to_index = {(a, t): i for i, (a, t) in
                    enumerate(zip(data["artists"], data["titles"]))}

    session = ort.InferenceSession(str((base / args.model).resolve()),
                                   providers=["CPUExecutionProvider"])

    quantized, reference, artists, albums = [], [], [], []
    for done, row in enumerate(subset, 1):
        chunk = windows_for(row["path"])
        key = (row["artist"], row["title"])
        if chunk is None or key not in key_to_index:
            continue

        vector = session.run(["embedding"], {"waveform": chunk})[0].mean(axis=0)
        quantized.append(vector)
        reference.append(data["embeddings"][key_to_index[key], 5, :])
        artists.append(row["artist"])
        albums.append(row["album"])

        if done % 50 == 0:
            print(f"  {done}/{len(subset)}", flush=True)

    names = sorted(set(artists))
    index = {n: i for i, n in enumerate(names)}
    artist_ids = np.array([index[a] for a in artists])
    album_ids = np.array([hash((a, b)) for a, b in zip(artists, albums)])

    print("\nна одной и той же подвыборке:")
    for label, matrix in (("PyTorch fp32", np.stack(reference)),
                          ("ONNX int8", np.stack(quantized))):
        t1, t5, counted = measure(matrix.astype(np.float32), artist_ids,
                                  album_ids, len(names))
        print(f"  {label:<14} top-1 {t1:5.1%}  top-5 {t5:5.1%}  ({counted} треков)")


if __name__ == "__main__":
    main()

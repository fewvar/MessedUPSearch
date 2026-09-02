"""
Какой слой MERT лучше всего различает артистов — решаем метрикой, а не на глаз.

Гоняем ту же честную проверку (leave-one-album-out), что и для PANNs, по каждому
слою отдельно, плюс по конкатенации нескольких лучших. Победителя и берём в ONNX.
"""
import argparse
from collections import defaultdict
from pathlib import Path

import numpy as np

TOP_K = 3
GALLERY_CAP = 40  # равная галерея у всех: иначе крупные артисты давят мелких


def normalize(matrix):
    centered = matrix - matrix.mean(axis=0, keepdims=True)
    return centered / (np.linalg.norm(centered, axis=1, keepdims=True) + 1e-9)


def score(embeddings, artist_ids, album_ids, n_artists, rng, per_artist_out=None):
    similarity = embeddings @ embeddings.T
    by_artist = {a: np.where(artist_ids == a)[0] for a in range(n_artists)}

    top1 = top5 = 0
    stats = defaultdict(lambda: [0, 0])
    confusion = defaultdict(int)

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
        truth = artist_ids[i]
        hit = ranking[0] == truth
        top1 += hit
        top5 += truth in ranking[:5]
        stats[truth][0] += hit
        stats[truth][1] += 1
        if not hit:
            confusion[(truth, ranking[0])] += 1

    if per_artist_out is not None:
        per_artist_out.update(stats)
        per_artist_out["__confusion__"] = confusion

    total = len(embeddings)
    return top1 / total, top5 / total


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--embeddings", default="data/embeddings_mert.npz")
    args = parser.parse_args()

    base = Path(__file__).resolve().parent.parent
    data = np.load((base / args.embeddings).resolve(), allow_pickle=True)

    raw = data["embeddings"]  # (N, слои, dim)
    artists, albums = data["artists"], data["albums"]

    names = sorted(set(artists.tolist()))
    index = {n: i for i, n in enumerate(names)}
    artist_ids = np.array([index[a] for a in artists])
    album_ids = np.array([hash((a, b)) for a, b in zip(artists, albums)])

    print(f"треков: {raw.shape[0]}, слоёв: {raw.shape[1]}, "
          f"размерность: {raw.shape[2]}, артистов: {len(names)}")
    print(f"\nслой | top-1 | top-5   (случайно {1/len(names):.1%})")

    results = []
    for layer in range(raw.shape[1]):
        rng = np.random.default_rng(0)
        t1, t5 = score(normalize(raw[:, layer, :]), artist_ids, album_ids, len(names), rng)
        results.append((t5, t1, layer))
        bar = "#" * int(t5 * 40)
        print(f" {layer:>3} | {t1:5.1%} | {t5:5.1%} {bar}")

    results.sort(reverse=True)
    best_layers = [layer for _, _, layer in results[:3]]
    print(f"\nлучшие слои по top-5: {best_layers}")

    # Конкатенация лучших слоёв: разные слои держат разную информацию,
    # вместе они иногда сильнее любого поодиночке.
    combo = np.concatenate([normalize(raw[:, layer, :]) for layer in best_layers], axis=1)
    combo = normalize(combo)
    rng = np.random.default_rng(0)
    t1, t5 = score(combo, artist_ids, album_ids, len(names), rng)
    print(f"конкатенация {best_layers}: top-1 {t1:.1%}, top-5 {t5:.1%}")

    # Разбор победителя
    best_layer = results[0][2]
    per_artist = {}
    rng = np.random.default_rng(0)
    score(normalize(raw[:, best_layer, :]), artist_ids, album_ids, len(names), rng, per_artist)
    confusion = per_artist.pop("__confusion__")

    print(f"\ntop-1 по артистам (слой {best_layer}):")
    rows = sorted(((h / max(1, n), names[a], h, n) for a, (h, n) in per_artist.items()),
                  reverse=True)
    for rate, name, hits, total in rows:
        print(f"  {name:<20} {rate:5.0%} {'#' * int(rate * 20):<20} ({hits}/{total})")

    print("\nчаще всего путает:")
    for (truth, wrong), count in sorted(confusion.items(), key=lambda kv: -kv[1])[:10]:
        print(f"  {names[truth]:<20} -> {names[wrong]:<20} {count}")


if __name__ == "__main__":
    main()

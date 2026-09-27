"""
Шаг 1 плана качества: «голый бит -> артист» на type beat'ах.

Запросы — type beat'ы с SoundCloud, подписанные артистом индекса друга
(crawler typebeats -> ffmpeg -> embed_mert.py, все 12 слоёв).
База — треки друга (embeddings_mert.npz, все 12 слоёв).

Что перебираем:
  слой           0..11 — слой 5 выбирали под «трек с голосом -> артист», для битов может быть другой;
  агрегация      как свести похожесть на треки артиста в одну оценку: top-1/3/5, среднее, центроид;
  поправка хабов из оценки артиста вычитается его средняя оценка по ДРУГИМ битам —
                 артист, который «похож на всё», перестаёт выигрывать у всех.

Метки шумные (битмейкеры ставят ники ради поиска), поэтому сравниваем варианты
между собой; разброс оцениваем бутстрепом по битам.
"""
import argparse
from collections import Counter
from pathlib import Path

import numpy as np

BASE = Path(__file__).resolve().parent.parent
AGGREGATIONS = ("top1", "top3", "top5", "mean", "centroid")


def unit(m):
    return m / (np.linalg.norm(m, axis=-1, keepdims=True) + 1e-9)


def artist_scores(Q, G, gallery_artists, n_artists, how):
    """Оценка каждого бита против каждого артиста: матрица (биты, артисты)."""
    sims = Q @ G.T
    scores = np.full((len(Q), n_artists), -np.inf, dtype=np.float32)

    for a in range(n_artists):
        idx = np.where(gallery_artists == a)[0]
        if idx.size == 0:
            continue
        if how == "centroid":
            scores[:, a] = Q @ unit(G[idx].mean(axis=0))
            continue
        part = sims[:, idx]
        if how == "mean":
            scores[:, a] = part.mean(axis=1)
        else:
            k = min(int(how[3:]), idx.size)
            scores[:, a] = np.sort(part, axis=1)[:, -k:].mean(axis=1)

    return scores


def debias(scores):
    """Вычесть среднюю оценку артиста по остальным битам (без самого бита)."""
    n = len(scores)
    total = scores.sum(axis=0, keepdims=True)
    others_mean = (total - scores) / max(1, n - 1)
    return scores - others_mean


def metrics(scores, truth):
    ranking = np.argsort(-scores, axis=1)
    ranks = np.array([np.where(ranking[i] == truth[i])[0][0] for i in range(len(truth))])
    top5_share = Counter(ranking[:, :5].ravel()).most_common(1)[0][1] / len(truth)
    return (ranks == 0).mean(), (ranks < 5).mean(), top5_share, ranks


def bootstrap_top5(ranks, n=500, seed=0):
    rng = np.random.default_rng(seed)
    samples = [(ranks[rng.integers(0, len(ranks), len(ranks))] < 5).mean() for _ in range(n)]
    return np.percentile(samples, [5, 95])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--gallery", default="data/embeddings_mert.npz")
    parser.add_argument("--queries", default="data/embeddings_typebeats.npz")
    args = parser.parse_args()

    gallery = np.load(BASE / args.gallery, allow_pickle=True)
    queries = np.load(BASE / args.queries, allow_pickle=True)

    names = sorted(set(gallery["artists"].tolist()))
    index = {n: i for i, n in enumerate(names)}
    g_artists = np.array([index[a] for a in gallery["artists"]])

    keep = np.array([a in index for a in queries["artists"]])
    q_artists = np.array([index[a] for a in queries["artists"][keep]])
    Graw, Qraw = gallery["embeddings"].astype(np.float32), queries["embeddings"][keep].astype(np.float32)

    n = len(names)
    print(f"битов: {len(q_artists)} по {len(set(q_artists))} артистам, база: {len(g_artists)} треков, {n} артистов")
    print(f"случайно: top-1 {1 / n:.1%}, top-5 {5 / n:.1%}\n")

    print("слой | top-1  top-5   (агрегация top3, без поправки)")
    by_layer = []
    for layer in range(Graw.shape[1]):
        center = Graw[:, layer].mean(axis=0)
        G, Q = unit(Graw[:, layer] - center), unit(Qraw[:, layer] - center)
        t1, t5, _, _ = metrics(artist_scores(Q, G, g_artists, n, "top3"), q_artists)
        by_layer.append((t5, t1, layer))
        print(f" {layer:>3} | {t1:5.1%}  {t5:5.1%} {'#' * int(t5 * 40)}")

    best_layers = [l for _, _, l in sorted(by_layer, reverse=True)[:3]]
    print(f"\nлучшие слои: {best_layers}; в приложении сейчас слой 5")

    print("\nслой  агрегация  поправка | top-1  top-5  [90% ДИ top-5]  доля самого частого в top-5")
    rows = []
    for layer in sorted(set(best_layers + [5])):
        center = Graw[:, layer].mean(axis=0)
        G, Q = unit(Graw[:, layer] - center), unit(Qraw[:, layer] - center)
        for how in AGGREGATIONS:
            raw = artist_scores(Q, G, g_artists, n, how)
            for label, scores in (("нет", raw), ("хабы", debias(raw))):
                t1, t5, hub, ranks = metrics(scores, q_artists)
                lo, hi = bootstrap_top5(ranks)
                rows.append((t5, t1, layer, how, label))
                print(f" {layer:>3}  {how:<9}  {label:<5}   | {t1:5.1%}  {t5:5.1%}  [{lo:5.1%}–{hi:5.1%}]  {hub:5.1%}")

    t5, t1, layer, how, label = max(rows)
    base = next(r for r in rows if r[2] == 5 and r[3] == "top3" and r[4] == "нет")
    print(f"\nсейчас в приложении (слой 5, top3, без поправки): top-1 {base[1]:.1%}, top-5 {base[0]:.1%}")
    print(f"лучший вариант: слой {layer}, {how}, поправка «{label}»: top-1 {t1:.1%}, top-5 {t5:.1%}")


if __name__ == "__main__":
    main()

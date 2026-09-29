"""
Шаг 2: type beat'ы против трёх вариантов базы из тех же превью Deezer —
  full  превью как есть (с голосом, как сейчас в индексе),
  inst  только инструментал (Demucs),
  mix   среднее векторов full и inst одного превью.
Метрика та же, что в evaluate_beats.py; слой 5 (оставлен по итогам шага 1), top-3,
с поправкой на хабы и без. Решение: переходим на inst/mix, только если выигрыш
выходит за 90% доверительный интервал.
"""
from pathlib import Path

import numpy as np

from evaluate_beats import artist_scores, bootstrap_top5, debias, metrics, unit

BASE = Path(__file__).resolve().parent.parent
LAYER = 5


def main():
    queries = np.load(BASE / "data/embeddings_typebeats.npz", allow_pickle=True)
    full = np.load(BASE / "data/embeddings_vocal_full.npz", allow_pickle=True)
    inst = np.load(BASE / "data/embeddings_vocal_inst.npz", allow_pickle=True)

    # Пары full/inst одного превью — по названию (id превью).
    inst_by_id = {t: i for i, t in enumerate(inst["titles"])}
    pairs = [(i, inst_by_id[t]) for i, t in enumerate(full["titles"]) if t in inst_by_id]
    artists = np.array([full["artists"][i] for i, _ in pairs])
    F = full["embeddings"][[i for i, _ in pairs], LAYER].astype(np.float32)
    I = inst["embeddings"][[j for _, j in pairs], LAYER].astype(np.float32)

    names = sorted(set(artists.tolist()))
    index = {n: k for k, n in enumerate(names)}
    g_artists = np.array([index[a] for a in artists])

    keep = np.array([a in index for a in queries["artists"]])
    truth = np.array([index[a] for a in queries["artists"][keep]])
    Qraw = queries["embeddings"][keep, LAYER].astype(np.float32)

    n = len(names)
    print(f"база: {len(pairs)} превью, {n} артистов; битов-запросов: {len(truth)}; случайно top-5 {5 / n:.1%}\n")
    print("вариант  поправка | top-1  top-5  [90% ДИ top-5]  доля самого частого в top-5")

    for label, raw in (("full", F), ("inst", I), ("mix", (F + I) / 2)):
        # Центр — свой у каждого варианта: инструментал сдвинут относительно полного трека.
        center = raw.mean(axis=0)
        G, Q = unit(raw - center), unit(Qraw - center)
        scores = artist_scores(Q, G, g_artists, n, "top3")
        for fix, s in (("нет", scores), ("хабы", debias(scores))):
            t1, t5, hub, ranks = metrics(s, truth)
            lo, hi = bootstrap_top5(ranks)
            print(f" {label:<6}  {fix:<5}    | {t1:5.1%}  {t5:5.1%}  [{lo:5.1%}–{hi:5.1%}]  {hub:5.1%}")


if __name__ == "__main__":
    main()

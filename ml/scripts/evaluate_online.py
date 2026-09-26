"""
Держит ли качество база, собранная из сети, против базы из архива друга.

Запросы — полные треки друга (слой 5 из embeddings_mert.npz), то есть «как будто
пользователь принёс трек». Галерея — либо треки друга (как раньше, leave-one-album-out),
либо векторы, которые MlCheck online посчитал по превью Deezer боевым C#-кодом.

Сравнение честное только на одном наборе артистов, поэтому все варианты считаются
по тем артистам, которые нашлись на Deezer. Центр везде один — среднее по трекам
друга, ровно как в artist_index.bin и в приложении.

Ловушка: топ Deezer и архив друга пересекаются по песням. Превью той же песни —
это не «похожий артист», а узнавание трека. Поэтому есть вариант, где онлайн-треки
с тем же названием выкидываются из галереи для этого запроса.
"""
import argparse
import re
from pathlib import Path

import numpy as np

LAYER = 5
TOP_K = 3


def unit(matrix):
    return matrix / (np.linalg.norm(matrix, axis=1, keepdims=True) + 1e-9)


def simplify(title):
    title = title.lower()
    title = title.split(" - ", 1)[-1]                 # «2hollis - GOD» -> «god»
    title = re.sub(r"[\(\[].*?[\)\]]", "", title)      # (feat. ...), [prod. ...]
    return re.sub(r"[^\w]", "", title)


def rank_artists(similarities, gallery_artists, allowed, n_artists):
    scores = np.full(n_artists, -np.inf, dtype=np.float32)
    for artist in range(n_artists):
        values = similarities[(gallery_artists == artist) & allowed]
        if values.size == 0:
            continue
        if values.size > TOP_K:
            values = np.partition(values, -TOP_K)[-TOP_K:]
        scores[artist] = values.mean()
    return np.argsort(-scores), np.isfinite(scores).sum()


def evaluate(queries, query_artists, gallery, gallery_artists, n_artists, mask_for):
    similarity = queries @ gallery.T
    top1 = top5 = counted = 0

    for i in range(len(queries)):
        ranking, available = rank_artists(similarity[i], gallery_artists, mask_for(i), n_artists)
        if available < 5:
            continue
        truth = query_artists[i]
        top1 += ranking[0] == truth
        top5 += truth in ranking[:5]
        counted += 1

    return top1 / max(1, counted), top5 / max(1, counted), counted


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--embeddings", default="data/embeddings_mert.npz")
    parser.add_argument("--online", default="data/online_deezer")
    parser.add_argument("--cap", type=int, default=10, help="треков друга на артиста в урезанной базе")
    args = parser.parse_args()

    base = Path(__file__).resolve().parent.parent
    data = np.load(base / args.embeddings, allow_pickle=True)
    friend_raw = data["embeddings"][:, LAYER, :].astype(np.float32)
    friend_names = data["artists"].astype(str)
    albums = data["albums"].astype(str)
    titles = data["titles"].astype(str)

    center = friend_raw.mean(axis=0)

    online_dir = base / args.online
    online_raw = np.fromfile(online_dir / "vectors.f32", dtype=np.float32).reshape(-1, friend_raw.shape[1])
    meta = [line.split("\t") for line in (online_dir / "meta.tsv").read_text().splitlines() if line]
    online_names = np.array([m[0] for m in meta])
    online_titles = [simplify(m[1]) for m in meta]
    assert len(meta) == len(online_raw), "vectors.f32 и meta.tsv разной длины"

    names = sorted(set(online_names.tolist()))
    index = {n: i for i, n in enumerate(names)}

    keep = np.isin(friend_names, names)
    queries = unit(friend_raw[keep] - center)
    query_artists = np.array([index[n] for n in friend_names[keep]])
    query_albums = albums[keep]
    query_titles = [simplify(t) for t in titles[keep]]

    online = unit(online_raw - center)
    online_artists = np.array([index[n] for n in online_names])

    print(f"артистов на Deezer: {len(names)} из {len(set(friend_names))}, "
          f"запросов: {len(queries)}, превью: {len(online)} "
          f"(в среднем {len(online) / len(names):.1f} на артиста)")
    print(f"случайное угадывание: top-1 {1 / len(names):.1%}, top-5 {5 / len(names):.1%}\n")

    rng = np.random.default_rng(0)

    # База друга, как раньше: галерея без альбома запроса.
    def friend_mask(cap):
        def mask(i):
            allowed = query_albums != query_albums[i]
            if cap is None:
                return allowed
            result = np.zeros_like(allowed)
            for artist in range(len(names)):
                pool = np.where(allowed & (query_artists == artist))[0]
                if pool.size > cap:
                    pool = rng.choice(pool, cap, replace=False)
                result[pool] = True
            return result
        return mask

    same_song = np.array([[q == o or (len(o) > 3 and o in q) for o in online_titles] for q in query_titles])

    rows = [
        ("друг, вся база (leave-one-album-out)", queries, query_artists, friend_mask(None)),
        (f"друг, по {args.cap} треков на артиста", queries, query_artists, friend_mask(args.cap)),
        ("Deezer-превью, как есть", online, online_artists, lambda i: np.ones(len(online), bool)),
        ("Deezer-превью, без той же песни", online, online_artists, lambda i: ~same_song[i]),
    ]

    print(f"{'галерея':<38} top-1   top-5   запросов")
    for label, gallery, gallery_artists, mask in rows:
        t1, t5, n = evaluate(queries, query_artists, gallery, gallery_artists, len(names), mask)
        print(f"{label:<38} {t1:5.1%}   {t5:5.1%}   {n}")

    print(f"\nзапросов, у которых та же песня есть среди превью: {same_song.any(axis=1).sum()}")


if __name__ == "__main__":
    main()

"""
Сравнение моделей на всей галерее (tmp/plans/models-v0.7.md) — по data/models/<модель>.npz.

Галерея: 30 ориентиров + 652 андеграунда индекса (artists_index_v2.bin, по source_url в crawl.db).
Ориентиры — два варианта: превью Deezer (все 30) и куски SoundCloud тем же конвейером (где есть).
Запросы — type beat'ы. Для каждой модели/варианта/слоя:
  центр — среднее всех векторов галереи; оценка артиста — среднее top-3 треков;
  поправка на хабы — 5 групп по артистам, фон только из битов других групп (без утечки).
Парный бутстреп против MERT слой 5 (как в приложении).

  python compare_models.py
"""
import csv
import sqlite3
from pathlib import Path

import numpy as np


BASE = Path(__file__).resolve().parent.parent
TOP_K = 3
FOLDS = 5
MIN_TRACKS = 2


def norm(text):
    return "".join(c for c in text.lower() if c.isalnum())


def unit(m):
    return m / (np.linalg.norm(m, axis=-1, keepdims=True) + 1e-9)


def labels():
    """key -> (роль, артист). Роль: beat / deezer / sc / und."""
    beats = {r["track_id"]: r["artist"] for r in csv.DictReader((BASE / "data/typebeats/typebeats.csv").open(encoding="utf-8-sig"))}
    names = sorted(set(beats.values()))
    by_norm = {norm(n): n for n in names}

    und_urls = {meta_url for meta_url in read_index_urls()}
    main = sqlite3.connect(BASE / "data/crawl.db")
    und = {str(t): u for t, u in main.execute("SELECT t.id, a.source_url FROM tracks t JOIN artists a ON a.id = t.artist_id")
           if u in und_urls}
    refs = {str(t): n for t, n in sqlite3.connect(BASE / "data/crawl_refs.db").execute(
        "SELECT t.id, a.nickname FROM tracks t JOIN artists a ON a.id = t.artist_id")}

    def label(key):
        group, ident = key.split("/", 1)
        if group == "beats":
            return ("beat", beats.get(ident))
        if group == "refs_deezer":
            return ("deezer", by_norm.get(norm(ident.split("__")[0])))
        if group == "refs_sc":
            name = by_norm.get(norm(refs.get(ident, "")))
            return ("sc", None if name == "Autumn!" else name)   # seed-refs нашёл чужой профиль
        if group == "underground":
            return ("und", und.get(ident))
        return (None, None)

    return label


def read_index_urls():
    """source_url артистов индекса (строка метаданных №4)."""
    import struct
    urls = []
    with open(BASE / "data/artists_index_v2.bin", "rb") as f:
        assert f.read(4) == b"MUSX"
        _, dim, count = struct.unpack("<iii", f.read(12))
        f.read(4 * dim)
        for _ in range(count):
            meta = []
            for _ in range(9):
                (n,) = struct.unpack("<i", f.read(4))
                meta.append(f.read(n).decode("utf-8"))
            _, tracks = struct.unpack("<ii", f.read(8))
            f.read(2 * dim * tracks)
            urls.append(meta[3])
    return urls


def evaluate(X, roles, artists, ref_role):
    """X — векторы всех файлов одной модели/варианта. Возвращает ранги битов среди 30 и среди всех."""
    is_ref = roles == ref_role
    is_und = roles == "und"
    beat = roles == "beat"

    ref_names = sorted(set(artists[is_ref]))
    und_names = sorted(n for n, c in zip(*np.unique(artists[is_und], return_counts=True)) if c >= MIN_TRACKS)
    gallery_names = ref_names + und_names
    index = {n: i for i, n in enumerate(gallery_names)}
    n_ref, n_all = len(ref_names), len(gallery_names)

    in_gallery = (is_ref | is_und) & np.isin(artists, gallery_names)
    G_raw = X[in_gallery]
    center = G_raw.mean(axis=0)
    G = unit(G_raw - center)
    ga = np.array([index[a] for a in artists[in_gallery]])

    qmask = beat & np.isin(artists, ref_names)
    Q = unit(X[qmask] - center)
    qa = np.array([index[a] for a in artists[qmask]])

    sims = Q @ G.T
    scores = np.full((len(Q), n_all), -np.inf, dtype=np.float32)
    for a in range(n_all):
        part = sims[:, ga == a]
        k = min(TOP_K, part.shape[1])
        scores[:, a] = np.sort(part, axis=1)[:, -k:].mean(axis=1)

    order = np.random.default_rng(0).permutation(n_ref)
    rank30 = np.zeros(len(Q), int)
    rank_all = np.zeros(len(Q), int)
    for held in np.array_split(order, FOLDS):
        test = np.isin(qa, held)
        s = scores - scores[~test].mean(axis=0, keepdims=True)
        s = s[test]
        truth = s[np.arange(len(s)), qa[test]]
        rank_all[test] = (s > truth[:, None]).sum(axis=1)
        rank30[test] = (s[:, :n_ref] > truth[:, None]).sum(axis=1)

    beat_ids = np.flatnonzero(qmask)
    return beat_ids, rank30, rank_all, n_ref, n_all


def bootstrap(a, b, reps=2000):
    rng = np.random.default_rng(0)
    diffs = []
    for _ in range(reps):
        k = rng.integers(0, len(a), len(a))
        diffs.append(a[k].mean() - b[k].mean())
    lo, hi = np.percentile(diffs, [5, 95])
    return lo * 100, hi * 100


def main():
    label = labels()
    results = {}

    for path in sorted((BASE / "data/models").glob("*.npz")):
        z = np.load(path, allow_pickle=True)
        keys = z["keys"].astype(str)
        lab = [label(k) for k in keys]
        roles = np.array([r or "" for r, _ in lab])
        artists = np.array([a or "" for _, a in lab])
        ok = artists != ""

        for variant in (k for k in z.files if k != "keys"):
            data = z[variant]
            layers = range(data.shape[1]) if data.ndim == 3 else [None]
            for layer in layers:
                X = (data[:, layer] if layer is not None else data)[ok].astype(np.float32)
                name = f"{path.stem}/{variant}" + (f"/{layer}" if layer is not None else "")
                for ref_role in ("deezer", "sc"):
                    results[(name, ref_role)] = evaluate(X, roles[ok], artists[ok], ref_role) + (keys[ok],)

    for ref_role, title in (("deezer", "ориентиры — превью Deezer"), ("sc", "ориентиры — SoundCloud, тот же конвейер")):
        rows = {n: r for (n, role), r in results.items() if role == ref_role}
        base_name = "mert/layer5"
        if base_name not in rows:
            print("нет базовой линии mert/layer5")
            return
        b_ids, b30, ball, n_ref, n_all = rows[base_name][:5]
        b_keys = rows[base_name][5][b_ids]
        print(f"\n== {title}: {n_ref} ориентиров, всего {n_all} артистов, битов {len(b_ids)}; "
              f"случайно top-5/682 = {5 / n_all:.2%}")
        print(f"{'модель':<24} {'top-5/30':>9} {'top-5/все':>10} {'top-10/все':>11} {'медиана':>8}   "
              f"top-10/все против MERT, 90% ДИ")

        # Для MuQ показываем только лучший слой по top-10 среди всех (выбор на тех же данных — отмечено).
        best = {}
        for name, r in rows.items():
            family = name.rsplit("/", 1)[0] if name.startswith("muq/") else name
            score = (r[2] < 10).mean()
            if family not in best or score > best[family][0]:
                best[family] = (score, name)

        for family, (_, name) in sorted(best.items(), key=lambda x: -x[1][0]):
            ids, r30, rall, _, _ = rows[name][:5]
            keys = rows[name][5][ids]
            common = np.intersect1d(keys, b_keys)
            a = (rall[np.isin(keys, common)][np.argsort(keys[np.isin(keys, common)])] < 10).astype(float)
            b = (ball[np.isin(b_keys, common)][np.argsort(b_keys[np.isin(b_keys, common)])] < 10).astype(float)
            lo, hi = bootstrap(a, b) if name != base_name else (0.0, 0.0)
            mark = " *" if name.startswith("muq/") else ""
            print(f"{name + mark:<24} {(r30 < 5).mean():>8.1%} {(rall < 5).mean():>10.1%} {(rall < 10).mean():>11.1%} "
                  f"{int(np.median(rall)) + 1:>8}   {'' if name == base_name else f'{(a.mean() - b.mean()) * 100:+.1f} [{lo:+.1f}; {hi:+.1f}]'}")
    print("\n* MuQ: слой выбран по этим же данным — цифра слегка оптимистична")


if __name__ == "__main__":
    main()

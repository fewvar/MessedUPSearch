"""
Этап В плана bpm-v0.7: «что получилось вообще» на всём индексе.

Галерея = 30 ориентиров (references_v2.bin) + 652 андеграунд-артиста (artists_index_v2.bin),
всего ~682. Запросы — type beat'ы ориентиров. Вопросы:
  1. находится ли нужный артист среди 682, а не среди 30 (случайно top-5 ≈ 0.7%);
  2. сколько андеграунда пролезает выше него — насколько «соседи» по звуку перемешаны;
  3. контроль: среди одних 30 должно выйти то же, что в train_head (41.9%).

Поправка на хабы — как в приложении, но фон в каждой группе только из битов других артистов
(5 групп по артистам, как в train_head), чтобы не было утечки.

  python evaluate_all.py
"""
import argparse
import sqlite3
import struct
from pathlib import Path

import numpy as np

from train_head import FOLDS, TOP_K, load, load_center, unit

BASE = Path(__file__).resolve().parent.parent
TARGETS = BASE / "data/artists_index_v2.bin"


def read_index(path):
    """TargetIndex v2 -> (ники, векторы, номер артиста у каждого вектора). Векторы уже центрированы."""
    def string(f):
        (n,) = struct.unpack("<i", f.read(4))
        return f.read(n).decode("utf-8")

    names, vectors, owner = [], [], []
    with open(path, "rb") as f:
        assert f.read(4) == b"MUSX"
        version, dim, count = struct.unpack("<iii", f.read(12))
        assert version == 2
        f.read(4 * dim)
        for a in range(count):
            meta = [string(f) for _ in range(9)]
            _, tracks = struct.unpack("<ii", f.read(8))
            rows = np.frombuffer(f.read(2 * dim * tracks), "<f2").astype(np.float32).reshape(tracks, dim)
            names.append(meta[0])
            vectors.append(rows)
            owner += [a] * tracks
    return names, np.concatenate(vectors), np.array(owner)


def read_refs_db(path, names, exclude):
    """Ориентиры, прогнанные краулером (seed-refs): inst-векторы из базы, центр — общий v2."""
    index = {n: i for i, n in enumerate(names)}
    center = load_center()
    rows = sqlite3.connect(path).execute(
        "SELECT a.nickname, e.vector FROM embeddings e JOIN tracks t ON t.id = e.track_id "
        "JOIN artists a ON a.id = t.artist_id WHERE e.kind = 'inst'").fetchall()
    rows = [(n, v) for n, v in rows if n in index and n not in exclude]
    G = unit(np.stack([np.frombuffer(v, "<f4") for _, v in rows]) - center)
    ga = np.array([index[n] for n, _ in rows])
    return G, ga


def scores(Q, G, ga, n):
    sims = Q @ G.T
    out = np.full((len(Q), n), -np.inf, dtype=np.float32)
    for a in range(n):
        part = sims[:, ga == a]
        k = min(TOP_K, part.shape[1])
        out[:, a] = np.sort(part, axis=1)[:, -k:].mean(axis=1)
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--refs-db", help="ориентиры из базы краулера вместо превью Deezer")
    parser.add_argument("--exclude", default="", help="ники через запятую — не участвуют (неверный профиль и т.п.)")
    args = parser.parse_args()

    ref_names, G, ga, Q, qa = load()
    exclude = {x for x in args.exclude.split(",") if x}
    if args.refs_db:
        G, ga = read_refs_db(args.refs_db, ref_names, exclude)
    # Биты артистов без векторов в галерее не считаем: их нельзя найти в принципе.
    have = np.isin(qa, np.unique(ga)) & ~np.isin(qa, [ref_names.index(x) for x in exclude if x in ref_names])
    Q, qa = Q[have], qa[have]
    print(f"источник ориентиров: {args.refs_db or 'превью Deezer'}; ориентиров с векторами {len(np.unique(ga))}")
    n_ref = len(ref_names)
    und_names, U, ua = read_index(TARGETS)

    clash = {x.lower() for x in ref_names} & {x.lower() for x in und_names}
    print(f"ориентиров {n_ref}, андеграунд {len(und_names)} (векторов {len(U)}), битов {len(Q)}; "
          f"совпадений по нику: {sorted(clash) or 'нет'}")

    all_G = np.concatenate([G, U])
    all_ga = np.concatenate([ga, ua + n_ref])
    n_all = n_ref + len(und_names)
    print(f"галерея {n_all} артистов; случайно top-5 = {5 / n_all:.2%}\n")

    raw = scores(Q, all_G, all_ga, n_all)

    order = np.random.default_rng(0).permutation(n_ref)
    folds = np.array_split(order, FOLDS)
    rank30, rank_all, above = [], [], []
    und_in_top10 = []

    for held in folds:
        test = np.isin(qa, held)
        corrected = raw - raw[~test].mean(axis=0, keepdims=True)
        s = corrected[test]
        truth = qa[test]

        true_score = s[np.arange(len(s)), truth]
        rank_all.append((s > true_score[:, None]).sum(axis=1))
        rank30.append((s[:, :n_ref] > true_score[:, None]).sum(axis=1))
        above.append((s[:, n_ref:] > true_score[:, None]).sum(axis=1))

        top10 = np.argsort(-s, axis=1)[:, :10]
        und_in_top10.append((top10 >= n_ref).sum(axis=1))

    rank30, rank_all, above = map(np.concatenate, (rank30, rank_all, above))
    und_in_top10 = np.concatenate(und_in_top10)

    print(f"среди 30 ориентиров:   top-1 {(rank30 == 0).mean():.1%}  top-5 {(rank30 < 5).mean():.1%}  (контроль, ждём 41.9%)")
    print(f"среди всех {n_all}:    top-1 {(rank_all == 0).mean():.1%}  top-5 {(rank_all < 5).mean():.1%}  "
          f"top-10 {(rank_all < 10).mean():.1%}  top-50 {(rank_all < 50).mean():.1%}")
    print(f"медианное место нужного артиста: {int(np.median(rank_all)) + 1} из {n_all}")
    print(f"андеграунда выше нужного: медиана {int(np.median(above))}, "
          f"ни одного — у {(above == 0).mean():.0%} битов")
    print(f"андеграунда в топ-10 бита: в среднем {und_in_top10.mean():.1f} из 10")

    # Кто из андеграунда чаще всего лезет в топ-10 — остаточные хабы в большом индексе.
    counts = np.zeros(n_all, int)
    for held in folds:
        test = np.isin(qa, held)
        corrected = raw - raw[~test].mean(axis=0, keepdims=True)
        for row in np.argsort(-corrected[test], axis=1)[:, :10]:
            counts[row] += 1
    top = np.argsort(-counts)[:5]
    names = ref_names + und_names
    print("чаще всех в топ-10: " + ", ".join(f"{names[i]} {counts[i] / len(Q):.0%}" for i in top))


if __name__ == "__main__":
    main()

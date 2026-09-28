"""
Шаг 4 плана качества: даёт ли темп что-то поверх звука (tmp/plans/bpm-v0.7.md, этап А).

  1. темп битов линейки (typebeats24) и превью Deezer 30 ориентиров — librosa, кэш в data/tempo.json;
  2. проверка детектора на битах, где темп написан в названии;
  3. разброс темпа у артистов;
  4. линейка: как сейчас (MERT + хабы) против MERT + темп (мягкий штраф / жёсткий отсев),
     параметры подбираются на 4 группах артистов и проверяются на 5-й (как у головы);
  5. «только темп» без звука — базовая линия консилиума.

Темп сравнивается со сворачиванием октав: 70, 140 и 280 — одно и то же (half-time в трэпе
детекторы путают постоянно). Расстояние — в октавах, от 0 до 0.5.

  nice -n 10 python bpm_eval.py
"""
import csv
import json
import re
from pathlib import Path

import numpy as np

from train_head import FOLDS, LAYER, load_center, unit, artist_scores

BASE = Path(__file__).resolve().parent.parent
CACHE = BASE / "data/tempo_plp.json"
TITLE_BPM = re.compile(r"(\d{2,3})\s*bpm", re.IGNORECASE)
NEAREST = 3             # темп артиста для бита — среднее по 3 ближайшим трекам (как top-3 в звуке)


def detect(path):
    import librosa

    # PLP с шагом 256: на битах с темпом в названии 75% точно против 46% у beat_track
    # (у того шаг 512 — сетка ~3 BPM на 140, и он чаще сбивается на 2/3 темпа).
    y, sr = librosa.load(path, sr=22050, mono=True)
    onset = librosa.onset.onset_strength(y=y, sr=sr, hop_length=256)
    pulse = librosa.beat.plp(onset_envelope=onset, sr=sr, hop_length=256)
    return float(librosa.feature.tempo(onset_envelope=pulse, sr=sr, hop_length=256)[0])


def folded(a, b):
    """Расстояние между темпами в октавах по модулю октавы: 0 — совпадают (или ×2), 0.5 — дальше некуда."""
    d = np.abs(np.log2(np.asarray(a) / np.asarray(b))) % 1.0
    return np.minimum(d, 1.0 - d)


def tempos(paths):
    cache = json.loads(CACHE.read_text()) if CACHE.exists() else {}
    todo = [p for p in paths if str(p) not in cache]
    for i, p in enumerate(todo, 1):
        try:
            cache[str(p)] = detect(p)
        except Exception as ex:
            print(f"\nпропуск {p.name}: {ex}")
            cache[str(p)] = None
        if i % 50 == 0 or i == len(todo):
            print(f"\rтемп: {i}/{len(todo)}", end="", flush=True)
            CACHE.write_text(json.dumps(cache))
    if todo:
        print()
    return [cache[str(p)] for p in paths]


def load():
    manifest = list(csv.DictReader((BASE / "data/manifest_typebeats.csv").open(encoding="utf-8")))
    q = np.load(BASE / "data/embeddings_typebeats.npz", allow_pickle=True)
    g = np.load(BASE / "data/embeddings_vocal_inst.npz", allow_pickle=True)
    assert len(manifest) == len(q["artists"]) and all(m["artist"] == a for m, a in zip(manifest, q["artists"]))

    names = sorted(set(g["artists"].astype(str)) & set(q["artists"].astype(str)))
    index = {n: i for i, n in enumerate(names)}
    center = load_center()

    beat_tempo = np.array(tempos([Path(m["path"]) for m in manifest]), dtype=float)
    keep = np.isin(q["artists"].astype(str), names) & np.isfinite(beat_tempo) & (beat_tempo > 0)
    Q = unit(q["embeddings"][keep, LAYER].astype(np.float32) - center)
    qa = np.array([index[a] for a in q["artists"][keep].astype(str)])
    titles = q["titles"][keep].astype(str)
    beat_tempo = beat_tempo[keep]

    gk = np.isin(g["artists"].astype(str), names)
    G = unit(g["embeddings"][gk, LAYER].astype(np.float32) - center)
    ga = np.array([index[a] for a in g["artists"][gk].astype(str)])

    # Темп артиста — по ВСЕМ его превью (с голосом: темп задаёт бит, голос детектору не мешает).
    previews = sorted((BASE / "data/vocal_test/previews").glob("*/*.mp3"))
    pt = tempos(previews)
    artist_tempos = {i: [] for i in range(len(names))}
    for p, t in zip(previews, pt):
        if p.parent.name in index and t:
            artist_tempos[index[p.parent.name]].append(t)

    return names, Q, qa, titles, beat_tempo, G, ga, artist_tempos


def tempo_distance(beat_tempo, artist_tempos, n):
    """(биты, артисты): среднее расстояние до NEAREST ближайших по темпу треков артиста."""
    out = np.full((len(beat_tempo), n), 0.5)
    for a in range(n):
        ts = np.array(artist_tempos[a])
        if len(ts) == 0:
            continue
        d = folded(beat_tempo[:, None], ts[None, :])
        k = min(NEAREST, d.shape[1])
        out[:, a] = np.sort(d, axis=1)[:, :k].mean(axis=1)
    return out


def rank_of(scores, truth):
    order = np.argsort(-scores, axis=1, kind="stable")
    return np.array([np.where(order[i] == truth[i])[0][0] for i in range(len(truth))])


def check_detector(titles, beat_tempo):
    rows = [(int(m.group(1)), t) for title, t in zip(titles, beat_tempo) if (m := TITLE_BPM.search(title))]
    rows = [(a, t) for a, t in rows if 50 <= a <= 250]
    if not rows:
        return
    stated, found = map(np.array, zip(*rows))
    exact = (np.abs(found / stated - 1) < 0.03).mean()
    octave = (folded(found, stated) < np.log2(1.03)).mean()
    print(f"детектор против темпа в названии ({len(rows)} битов): точно ±3% {exact:.0%}, "
          f"до октавы {octave:.0%}")


def evaluate():
    names, Q, qa, titles, beat_tempo, G, ga, artist_tempos = load()
    n = len(names)
    print(f"\nартистов {n}, битов {len(Q)}; случайно top-5 = {5 / n:.1%}")
    check_detector(titles, beat_tempo)

    spreads = []
    for a in range(n):
        ts = np.array(artist_tempos[a])
        if len(ts) > 1:
            # разброс: среднее попарное расстояние по темпу между треками артиста
            spreads.append(folded(ts[:, None], ts[None, :])[np.triu_indices(len(ts), 1)].mean())
    rnd = folded(beat_tempo[:, None], beat_tempo[None, ::7]).mean()
    print(f"разброс темпа внутри артиста {np.mean(spreads):.3f} окт. против {rnd:.3f} между случайными битами")

    D = tempo_distance(beat_tempo, artist_tempos, n)

    order = np.random.default_rng(0).permutation(n)
    folds = np.array_split(order, FOLDS)

    weights = [0.0, 0.05, 0.1, 0.2, 0.3, 0.5, 1.0]
    thresholds = [0.05, 0.08, 0.1, 0.15, 0.2]
    base, soft, hard, only = [], [], [], []
    chosen_w, chosen_t = [], []

    for held in folds:
        test = np.isin(qa, held)
        # хабы: фон — биты обучающих артистов, как в train_head
        sound = artist_scores(Q, G, ga, n) - artist_scores(Q[~test], G, ga, n).mean(axis=0, keepdims=True)

        def top5(scores, mask):
            return (rank_of(scores[mask], qa[mask]) < 5).mean()

        w = max(weights, key=lambda w: top5(sound - w * D, ~test))
        t = max(thresholds, key=lambda t: top5(sound - 10.0 * (D > t), ~test))
        chosen_w.append(w)
        chosen_t.append(t)

        base.append(rank_of(sound[test], qa[test]))
        soft.append(rank_of((sound - w * D)[test], qa[test]))
        hard.append(rank_of((sound - 10.0 * (D > t))[test], qa[test]))
        # «только темп»: ближе по темпу — выше; равные разбиваем случайно
        jitter = np.random.default_rng(1).random(D[test].shape) * 1e-6
        only.append(rank_of(-D[test] + jitter, qa[test]))

    base = np.concatenate(base)
    print(f"\nкак сейчас (MERT + хабы):  top-1 {(base == 0).mean():.1%}  top-5 {(base < 5).mean():.1%}")

    rng = np.random.default_rng(0)
    for label, ranks, extra in (("мягкий штраф", soft, f"w по группам {chosen_w}"),
                                ("жёсткий отсев", hard, f"порог {chosen_t}"),
                                ("только темп", only, "")):
        r = np.concatenate(ranks)
        diffs = []
        for _ in range(2000):
            k = rng.integers(0, len(base), len(base))
            diffs.append((r[k] < 5).mean() - (base[k] < 5).mean())
        lo, hi = np.percentile(diffs, [5, 95])
        print(f"{label:<14} top-1 {(r == 0).mean():.1%}  top-5 {(r < 5).mean():.1%}  "
              f"разница {np.mean(diffs) * 100:+.1f} п.п. [90% ДИ {lo * 100:+.1f}; {hi * 100:+.1f}]  {extra}")


if __name__ == "__main__":
    evaluate()

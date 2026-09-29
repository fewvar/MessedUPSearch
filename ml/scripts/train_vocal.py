"""
Классификатор «есть голос / чистый бит» поверх векторов MERT — чтобы в индекс
«кому продать бит» не попадали битмейкеры, фонк-продюсеры и электроника,
которых не отсечь по названиям треков.

Примеры с голосом:
  - превью Deezer 30 рэперов из замера (data/online_deezer), посчитаны C#-кодом;
  - треки рэперов из краулера, где в названии feat./ft. — голос почти наверняка,
    и источник тот же (SoundCloud/Audius), что у негативов.
Без голоса: треки артистов, у которых «биты: N/M» в фильтре (type beat в названиях).

Второй источник позитивов нужен, чтобы модель не выучила «Deezer против SoundCloud»
вместо «голос против бита» — это проверяется отдельной строкой в отчёте.

Выход: data/vocal_lr.json — веса логистической регрессии по центрированным векторам.
"""
import json
import re
import sqlite3
import struct
from pathlib import Path

import numpy as np
from sklearn.linear_model import LogisticRegression
from sklearn.model_selection import StratifiedKFold, cross_val_predict
from sklearn.metrics import roc_auc_score

BASE = Path(__file__).resolve().parent.parent
FEAT = re.compile(r"\b(feat|ft)\.?\s", re.IGNORECASE)


def load_center():
    with open(BASE / "crawler/Mert/artist_index.bin", "rb") as f:
        _, dim = struct.unpack("<ii", f.read(8))
        return np.frombuffer(f.read(4 * dim), dtype=np.float32)


def unit(m):
    return m / (np.linalg.norm(m, axis=1, keepdims=True) + 1e-9)


def main():
    center = load_center()
    dim = center.size

    deezer = np.fromfile(BASE / "data/online_deezer/vectors.f32", dtype=np.float32).reshape(-1, dim)

    db = sqlite3.connect(BASE / "data/crawl.db")
    rows = db.execute("""
        SELECT a.nickname, a.producer_reason, t.title, v.vector
        FROM vectors v JOIN tracks t ON t.id = v.track_id JOIN artists a ON a.id = t.artist_id
    """).fetchall()

    crawl_pos = [np.frombuffer(r[3], dtype=np.float32) for r in rows if r[1] == "" and FEAT.search(r[2])]
    crawl_neg = [np.frombuffer(r[3], dtype=np.float32) for r in rows if r[1].startswith("биты:")]

    print(f"голос: Deezer {len(deezer)}, краулер feat. {len(crawl_pos)};  без голоса: {len(crawl_neg)}")
    if len(crawl_neg) < 30 or len(crawl_pos) < 20:
        print("мало примеров — сначала listen и listen --negatives")
        return

    X = unit(np.vstack([deezer, np.array(crawl_pos), np.array(crawl_neg)]) - center)
    y = np.array([1] * (len(deezer) + len(crawl_pos)) + [0] * len(crawl_neg))
    source = np.array(["deezer"] * len(deezer) + ["crawl"] * len(crawl_pos) + ["crawl"] * len(crawl_neg))

    model = LogisticRegression(C=1.0, class_weight="balanced", max_iter=2000)
    proba = cross_val_predict(model, X, y, cv=StratifiedKFold(5, shuffle=True, random_state=0),
                              method="predict_proba")[:, 1]

    print(f"\n5-fold: ROC AUC {roc_auc_score(y, proba):.3f}, точность {((proba > .5) == y).mean():.1%}")

    # Проверка на подмену задачи: только треки с одной площадки (краулер).
    crawl = source == "crawl"
    print(f"только краулер (одна площадка): ROC AUC {roc_auc_score(y[crawl], proba[crawl]):.3f}, "
          f"точность {((proba[crawl] > .5) == y[crawl]).mean():.1%}")

    model.fit(X, y)
    out = BASE / "data/vocal_lr.json"
    out.write_text(json.dumps({"w": model.coef_[0].astype(float).tolist(),
                               "b": float(model.intercept_[0]), "threshold": 0.5}))
    print(f"\nвеса: {out}")

    # Разбор по артистам, которых нет в обучении: средняя вероятность голоса.
    probs = model.predict_proba(unit(np.array([np.frombuffer(r[3], dtype=np.float32) for r in rows]) - center))[:, 1]
    by_artist = {}
    for (nick, reason, title, _), p in zip(rows, probs):
        if reason == "":
            by_artist.setdefault(nick, []).append(p)

    scored = sorted(((np.mean(v), k, len(v)) for k, v in by_artist.items()))
    print(f"\nрэперов с векторами: {len(scored)}; с голосом < 0.5: {sum(s < .5 for s, _, _ in scored)}")
    print("самые «безголосые»:")
    for s, nick, n in scored[:15]:
        print(f"  {s:.2f}  {nick} ({n})")
    print("самые «голосовые»:")
    for s, nick, n in scored[-8:]:
        print(f"  {s:.2f}  {nick} ({n})")


if __name__ == "__main__":
    main()

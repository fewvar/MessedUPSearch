"""
Эталон EffNet style + голова для приложения (tmp/plans/effnet-app-v0.7.md, шаг 1).

Пишет:
  Assets/Models/effnet_head.bin      окно Ханна, мел-матрица Essentia (96×257), c0, W (Half), c2
  Assets/Models/references_v3.bin    30 ориентиров: инструменталы Deezer + SoundCloud (Autumn! — только Deezer)
  Assets/Models/background_v3.bin    type beat'ы — фон для поправки на хабы
  crawl.db, embeddings kind='effnet' сырые 1280 для андеграунда (из data/models/effnet.npz) —
                                     из них `crawler export` соберёт artists_index_v3.bin тем же C#-кодом

Итоговый вектор: unit( unit( unit(v - c0) · W ) - c2 ), v — среднее 'embeddings' EffNet по патчам.
W — та самая голова, что прошла проверку (head_underground.train, seed 0), округлённая до Half:
дальше и Python, и C# работают с округлённой, чтобы совпадать до бита.

  python export_effnet.py
"""
import sqlite3
import struct
from pathlib import Path

import essentia.standard as es
import numpy as np

import compare_models as cm
import head_underground as hu

BASE = cm.BASE
MODELS = BASE.parent / "MessedUpSearchA/Assets/Models"
HEAD_MAGIC = b"MUSH"
DIM = 1280


def essentia_frontend():
    """Окно и мел-матрица ровно из Essentia (TensorflowInputMusiCNN): C# только умножает."""
    window = es.Windowing(type="hann", normalized=False, zeroPhase=False)(np.ones(512, np.float32))
    mel = es.MelBands(inputSize=257, numberBands=96, sampleRate=16000, highFrequencyBound=8000,
                      warpingFormula="slaneyMel", weighting="linear", normalize="unit_tri")
    matrix = np.stack([mel(np.eye(257, dtype=np.float32)[i]) for i in range(257)], 1)   # 96×257, по мощности
    frame = (np.random.default_rng(0).standard_normal(512) * 0.1).astype(np.float32)
    ref = es.TensorflowInputMusiCNN()(frame)
    mine = np.log10(1 + 10000 * (matrix @ np.abs(np.fft.rfft(frame * window)) ** 2))
    assert np.abs(ref - mine).max() < 1e-5, "мел не совпал с Essentia"
    return window.astype(np.float32), matrix.astype(np.float32)


def project(v, c0, W, c2=None):
    z = cm.unit(cm.unit(v - c0) @ W)
    return z if c2 is None else cm.unit(z - c2)


def write_head(path, window, mel, c0, W, c2):
    with open(path, "wb") as f:
        f.write(HEAD_MAGIC)
        f.write(struct.pack("<iiii", 1, 512, 96, DIM))
        f.write(window.astype("<f4").tobytes())
        f.write(mel.astype("<f4").tobytes())
        f.write(c0.astype("<f4").tobytes())
        f.write(W.astype("<f2").tobytes())
        f.write(c2.astype("<f4").tobytes())


def write_string(f, text):
    data = text.encode("utf-8")
    f.write(struct.pack("<i", len(data)))
    f.write(data)


def write_references(path, c2, groups):
    """TargetIndex v3: векторы уже финальные, центр = c2 (для справки)."""
    with open(path, "wb") as f:
        f.write(b"MUSX")
        f.write(struct.pack("<iii", 3, DIM, len(groups)))
        f.write(c2.astype("<f4").tobytes())
        for name, rows in sorted(groups.items()):
            for text in (name, "Reference", "", "", "", "", "", "", ""):
                write_string(f, text)
            f.write(struct.pack("<ii", 0, len(rows)))
            f.write(rows.astype("<f2").tobytes())


def write_background(path, rows):
    with open(path, "wb") as f:
        f.write(struct.pack("<ii", len(rows), DIM))
        f.write(rows.astype("<f2").tobytes())   # Half, как в индексах


def main():
    window, mel = essentia_frontend()

    z = np.load(BASE / "data/models/effnet.npz", allow_pickle=True)
    keys = z["keys"].astype(str)
    label = cm.labels()
    lab = [label(k) for k in keys]
    roles = np.array([r or "" for r, _ in lab])
    artists = np.array([a or "" for _, a in lab])
    ok = artists != ""
    raw = z["style"].astype(np.float32)

    # c0 и обучение — ровно как в head_underground.load / main (там проверка и проводилась).
    c0 = raw[ok].mean(0)
    X, r, a = hu.load("style")
    und = r == "und"
    counts = dict(zip(*np.unique(a[und], return_counts=True)))
    train_mask = und & np.array([counts.get(x, 0) >= cm.MIN_TRACKS for x in a])
    W, before, after, epochs = hu.train(X[train_mask], a[train_mask], DIM)
    W = W.astype(np.float16).astype(np.float32)
    print(f"голова: отложенные андеграунд-артисты {before:.1%} -> {after:.1%}, эпох {epochs}")

    # Проверка с ориентирами Deezer + SC вместе — так они поедут в приложение.
    merged = np.where(np.isin(roles, ["deezer", "sc"]), "ref", roles)
    Z = project(raw[ok], c0, W)
    ids, r30, ra, n_ref, n_all = cm.evaluate(Z, merged[ok], artists[ok], "ref")
    print(f"итог (Half W, ориентиры Deezer+SC): top-5/30 {(r30 < 5).mean():.1%}  top-5/все {(ra < 5).mean():.1%}  "
          f"top-10/все {(ra < 10).mean():.1%}  медиана {int(np.median(ra)) + 1} из {n_all}")

    # c2 — среднее галереи после проекции: ориентиры + андеграунд с ≥2 треками (как в evaluate).
    gallery = ok & (np.isin(merged, ["ref"]) | (roles == "und")) & \
              ~((roles == "und") & np.array([counts.get(x, 0) < cm.MIN_TRACKS for x in artists]))
    gallery &= ~((roles == "und") & (artists == ""))
    c2 = Z[gallery[ok]].mean(0)
    write_head(MODELS / "effnet_head.bin", window, mel, c0, W, c2)

    final = project(raw, c0, W, c2)
    refs = {}
    for i in np.flatnonzero(ok & (merged == "ref")):
        refs.setdefault(artists[i], []).append(final[i])
    write_references(MODELS / "references_v3.bin", c2, {n: np.stack(v) for n, v in refs.items()})
    beats = final[ok & (roles == "beat")]
    write_background(MODELS / "background_v3.bin", beats)
    print(f"ориентиров {len(refs)} ({sum(len(v) for v in refs.values())} треков), фон {len(beats)} битов")

    # Шкала процентов: оценка артиста (top-3) минус его средняя по фону — как HubCorrection в приложении.
    G = np.concatenate([np.stack(v) for v in refs.values()])
    owners = np.concatenate([[i] * len(v) for i, v in enumerate(refs.values())])
    sims = beats @ G.T
    scores = np.stack([np.sort(sims[:, owners == j], 1)[:, -3:].mean(1) for j in range(len(refs))], 1)
    adjusted = np.sort(scores - scores.mean(0), 1)[:, ::-1]
    for place in (0, 2, 9):
        if place < adjusted.shape[1]:
            print(f"поправленная оценка, место {place + 1}: 10–90 перцентиль "
                  f"{np.percentile(adjusted[:, place], 10):.3f}–{np.percentile(adjusted[:, place], 90):.3f}")

    # Сырые векторы андеграунда -> crawl.db (kind='effnet'): оттуда их возьмёт C#-экспорт.
    db = sqlite3.connect(BASE / "data/crawl.db")
    rows = [(int(k.split("/")[1]), raw[i].astype("<f4").tobytes()) for i, k in enumerate(keys) if k.startswith("underground/")]
    db.executemany("INSERT OR REPLACE INTO embeddings (track_id, kind, vector, seconds) VALUES (?, 'effnet', ?, 30)", rows)
    db.commit()
    print(f"в crawl.db записано effnet-векторов: {len(rows)}")


if __name__ == "__main__":
    main()

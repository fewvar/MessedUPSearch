"""
«Звучит как» по инструменталам: превью Deezer 30 артистов, вокал отделён Demucs
(prepare_vocal_test.py -> embed_mert.py -> data/embeddings_vocal_inst.npz, слой 5)
-> ml/crawler/Mert/references_v2.bin в формате TargetIndex (см. TargetIndex.cs).

Центр — среднее этих инструментальных векторов. Он же общий для индекса целей
(artists_index_v2.bin) и для фона хабов: инструменталы смещены относительно треков
с голосом, и старый центр из artist_index.bin для них не годится.
"""
import struct
from pathlib import Path

import numpy as np

LAYER = 5
BASE = Path(__file__).resolve().parent.parent
OUT = BASE / "crawler/Mert/references_v2.bin"


def write_string(f, text):
    data = text.encode("utf-8")
    f.write(struct.pack("<i", len(data)))
    f.write(data)


def main():
    data = np.load(BASE / "data/embeddings_vocal_inst.npz", allow_pickle=True)
    raw = data["embeddings"][:, LAYER, :].astype(np.float32)
    artists = data["artists"].astype(str)

    center = raw.mean(axis=0)
    vectors = raw - center
    vectors /= np.linalg.norm(vectors, axis=1, keepdims=True) + 1e-9

    names = sorted(set(artists.tolist()))
    with open(OUT, "wb") as f:
        f.write(b"MUSX")
        f.write(struct.pack("<iii", 2, center.size, len(names)))
        f.write(center.astype("<f4").tobytes())
        for name in names:
            rows = vectors[artists == name]
            for text in (name, "Deezer", "", "", "", "", "", "", ""):
                write_string(f, text)
            f.write(struct.pack("<ii", 0, len(rows)))
            f.write(rows.astype("<f2").tobytes())

    print(f"{OUT.name}: {len(names)} артистов, {len(vectors)} превью, {OUT.stat().st_size / 1024:.0f} КБ")


if __name__ == "__main__":
    main()

"""
Фон для поправки на хабы: сырые векторы (слой 5) type beat'ов из линейки ->
MessedUpSearchA/Assets/Models/background_beats.bin.

Зачем: в поиске «бит -> артист» есть артисты, похожие на всё подряд (у Yeat 153 трека —
среди них всегда найдутся три похожих), и они стоят в топе почти любого бита. Поправка:
из оценки артиста вычитается его средняя оценка по фоновым битам. Замер (evaluate_beats.py):
точность top-5 та же (41.5% против 40.7%), а самый частый артист в топ-5 — у 32% битов вместо 66%.

Формат: int количество, int размерность, затем векторы float32 подряд. Сырые, до центра —
центр приложение берёт из artist_index.bin, как для всего остального.
"""
import struct
from pathlib import Path

import numpy as np

LAYER = 5
BASE = Path(__file__).resolve().parent.parent


def main():
    data = np.load(BASE / "data/embeddings_typebeats.npz", allow_pickle=True)
    vectors = data["embeddings"][:, LAYER, :].astype(np.float32)

    out = BASE.parent / "MessedUpSearchA/Assets/Models/background_beats.bin"
    with open(out, "wb") as f:
        f.write(struct.pack("<ii", *vectors.shape))
        f.write(vectors.tobytes(order="C"))

    print(f"{out}: {vectors.shape[0]} битов, {out.stat().st_size / 1024:.0f} КБ")


if __name__ == "__main__":
    main()

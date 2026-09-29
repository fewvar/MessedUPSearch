"""
Сверка EffNet + голова: C# (MlCheck vectors) против Python на одних файлах (tmp/plans/effnet-app-v0.7.md, шаг 3).

Python-сторона — ровно конвейер эталона: librosa 16 кГц, центральные 60 с, мел Essentia, ONNX style,
среднее по патчам, голова из effnet_head.bin. Критерий: косинус финальных векторов ≥ 0.999.

  python check_effnet.py [--n 20]
  python check_effnet.py --bench     # точность на линейке с битами, посчитанными C# (data/check_effnet/beats_cs.bin)
"""
import argparse
import random
import struct
import subprocess
from pathlib import Path

import numpy as np

from embed_models import EffNet, load

BASE = Path(__file__).resolve().parent.parent
HEAD = BASE.parent / "MessedUpSearchA/Assets/Models/effnet_head.bin"
MAX_SECONDS = 60


def read_head():
    with open(HEAD, "rb") as f:
        assert f.read(4) == b"MUSH"
        _, frame, bands, dim = struct.unpack("<iiii", f.read(16))
        f.read(4 * frame + 4 * bands * (frame // 2 + 1))
        c0 = np.frombuffer(f.read(4 * dim), "<f4")
        W = np.frombuffer(f.read(2 * dim * dim), "<f2").astype(np.float32).reshape(dim, dim)
        c2 = np.frombuffer(f.read(4 * dim), "<f4")
    return c0, W, c2


def unit(v):
    return v / (np.linalg.norm(v, axis=-1, keepdims=True) + 1e-9)


def bench():
    """
    Галерея — Python-векторы (как в индексах v3), биты — C# (MlCheck vectors по beats.txt).
    Та же проверка, что export_effnet.py (ориентиры Deezer+SC); ждём ~52.6% / ~13.5%.
    """
    import compare_models as cm

    tmp = BASE / "data/check_effnet"
    paths = [Path(p) for p in (tmp / "beats.txt").read_text().split("\n") if p]
    with open(tmp / "beats_cs.bin", "rb") as f:
        count, dim = struct.unpack("<ii", f.read(8))
        cs_final = np.frombuffer(f.read(), "<f4").reshape(count, 2, dim)[:, 1]
    cs = {f"beats/{p.stem}": v for p, v in zip(paths, cs_final) if np.any(v)}

    z = np.load(BASE / "data/models/effnet.npz", allow_pickle=True)
    keys = z["keys"].astype(str)
    label = cm.labels()
    lab = [label(k) for k in keys]
    roles = np.array([r or "" for r, _ in lab])
    roles = np.where(np.isin(roles, ["deezer", "sc"]), "ref", roles)
    artists = np.array([a or "" for _, a in lab])
    ok = artists != ""

    c0, W, c2 = read_head()
    # evaluate сам вычтет центр галереи; финальные векторы уже центрированы c2 — это почти ноль, не мешает.
    py = unit(unit(unit(z["style"].astype(np.float32) - c0) @ W) - c2)
    for name, X in (("Python", py), ("C# биты", np.stack([cs.get(k, py[i]) for i, k in enumerate(keys)]))):
        ids, r30, ra, _, n_all = cm.evaluate(X[ok], roles[ok], artists[ok], "ref")
        print(f"{name:<9} top-5/30 {(r30 < 5).mean():.1%}  top-5/все {(ra < 5).mean():.1%}  "
              f"top-10/все {(ra < 10).mean():.1%}  медиана {int(np.median(ra)) + 1} из {n_all}")
    print(f"C#-векторов битов: {len(cs)} из {len(paths)}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--n", type=int, default=20)
    parser.add_argument("--bench", action="store_true")
    args = parser.parse_args()
    if args.bench:
        bench()
        return

    rng = random.Random(0)
    beats = sorted((BASE / "data/typebeats").glob("*/*.mp3"))
    inst = sorted((BASE / "data/inst/underground").glob("*.mp3"))
    files = rng.sample(beats, args.n) + rng.sample(inst, args.n)

    tmp = BASE / "data/check_effnet"
    tmp.mkdir(exist_ok=True)
    (tmp / "list.txt").write_text("\n".join(map(str, files)))
    subprocess.run(["dotnet", "run", "-v", "q", "--project", str(BASE / "csharp-check"), "--",
                    "vectors", str(tmp / "list.txt"), str(tmp / "cs.bin")], check=True)

    with open(tmp / "cs.bin", "rb") as f:
        count, dim = struct.unpack("<ii", f.read(8))
        data = np.frombuffer(f.read(), "<f4").reshape(count, 2, dim)
    cs_raw, cs_final = data[:, 0], data[:, 1]

    model = EffNet()
    c0, W, c2 = read_head()
    py_raw = []
    for path in files:
        y = load(path, EffNet.rate)
        cap = MAX_SECONDS * EffNet.rate
        if len(y) > cap:
            start = (len(y) - cap) // 2
            y = y[start:start + cap]
        py_raw.append(model(y)["style"])
    py_raw = np.stack(py_raw)
    py_final = unit(unit(unit(py_raw - c0) @ W) - c2)

    raw_cos = np.sum(unit(py_raw) * unit(cs_raw), axis=1)
    final_cos = np.sum(py_final * cs_final, axis=1)
    for name, cos in (("сырой EffNet", raw_cos), ("финальный", final_cos)):
        print(f"{name:<14} косинус: мин {cos.min():.5f}  медиана {np.median(cos):.5f}  "
              f"биты мин {cos[:args.n].min():.5f}  инструменталы мин {cos[args.n:].min():.5f}")
    worst = np.argsort(final_cos)[:3]
    print("худшие:", ", ".join(f"{files[i].name} {final_cos[i]:.4f}" for i in worst))
    print("КРИТЕРИЙ", "пройден" if final_cos.min() >= 0.999 else "НЕ пройден", "(≥ 0.999)")


if __name__ == "__main__":
    main()

"""
MERT -> ONNX + бинарный индекс артистов для C#.

Две вещи, без которых C# посчитает не то:
  1. Обрезаем модель до слоя 5 включительно. Он оказался лучшим по метрике,
     а верхние шесть слоёв только жрут место и время.
  2. Кладём в индекс средний вектор базы. Сравнение идёт по центрированным
     векторам, значит из эмбеддинга нового бита тоже надо вычесть этот центр —
     иначе косинусы поедут.

Формат artist_index.bin (little-endian):
    int32    N — сколько треков
    int32    D — размерность (768)
    float32  D    — средний вектор базы (центр)
    float32  N*D  — центрированные и нормализованные векторы треков
    int32    A — сколько артистов
    A раз:   int32 длина имени в UTF-8 + байты
    int32    N — индекс артиста для каждого трека
"""
import argparse
import csv
import struct
import warnings
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn

warnings.filterwarnings("ignore")

MODEL_NAME = "m-a-p/MERT-v1-95M"
BEST_LAYER = 5
SAMPLE_RATE = 24000
WINDOW_SAMPLES = 10 * SAMPLE_RATE


class MertEmbedder(nn.Module):
    """Сырой звук 24 кГц -> вектор 768. Нормализация входа внутри графа."""

    def __init__(self, model):
        super().__init__()
        self.model = model

    def forward(self, waveform):
        x = (waveform - waveform.mean(dim=1, keepdim=True)) / (
            waveform.std(dim=1, keepdim=True) + 1e-7)
        return self.model(x).last_hidden_state.mean(dim=1)


def build_model():
    from transformers import AutoModel

    model = AutoModel.from_pretrained(MODEL_NAME, trust_remote_code=True).eval()
    model.encoder.layers = nn.ModuleList(list(model.encoder.layers)[:BEST_LAYER + 1])
    return MertEmbedder(model).eval()


def export(model, path: Path):
    path.parent.mkdir(parents=True, exist_ok=True)
    dummy = torch.randn(1, WINDOW_SAMPLES)

    torch.onnx.export(
        model, (dummy,), str(path),
        input_names=["waveform"], output_names=["embedding"],
        dynamic_axes={"waveform": {0: "batch"}, "embedding": {0: "batch"}},
        opset_version=17, do_constant_folding=True, dynamo=False)

    print(f"ONNX: {path.name}, {path.stat().st_size / 1e6:.0f} МБ")


# Квантизация проверена и отвергнута (2026-09-02). int8 ужимает 208 МБ до 52,
# но роняет метрику с top-5 56% до 42% на контрольной подвыборке — это треть
# качества. Поканальная и uint8-версии тоже не спасают: косинус с fp32 остаётся
# 0.87-0.93. Трансформеры плохо переносят динамическую квантизацию активаций,
# в отличие от свёрточных сетей. fp16-конвертер отдельно ломает граф на Cast-ноде.
# Итог: возим fp32 и решаем размер доставкой, а не сжатием.


def verify(model, onnx_path: Path, audio_paths, tolerance=1e-4):
    """Сверяем PyTorch и ONNX на реальном звуке — числа должны совпадать."""
    import onnxruntime as ort
    import soundfile as sf

    windows = []
    for path in audio_paths:
        audio, _ = sf.read(path, dtype="float32", always_2d=False)
        if audio.ndim > 1:
            audio = audio.mean(axis=1)
        if audio.size < WINDOW_SAMPLES:
            continue
        start = (audio.size - WINDOW_SAMPLES) // 2
        windows.append(audio[start:start + WINDOW_SAMPLES])

    batch = np.stack(windows).astype(np.float32)
    print(f"сверка на {len(batch)} реальных фрагментах")

    with torch.no_grad():
        reference = model(torch.from_numpy(batch)).numpy()

    session = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
    actual = session.run(["embedding"], {"waveform": batch})[0]

    diff = np.abs(reference - actual).max()
    normed = lambda m: m / np.linalg.norm(m, axis=1, keepdims=True)
    cos = (normed(reference) * normed(actual)).sum(axis=1)

    print(f"  расхождение с PyTorch: {diff:.2e}"
          f"{'  OK' if diff < tolerance else '  ВЫШЕ ПОРОГА'}")
    print(f"  косинус: min {cos.min():.5f}, среднее {cos.mean():.5f}")

    if diff > tolerance:
        raise RuntimeError("ONNX считает не то же самое, что PyTorch")
    return float(cos.min())


def write_index(embeddings_path: Path, out_path: Path):
    data = np.load(embeddings_path, allow_pickle=True)
    vectors = data["embeddings"][:, BEST_LAYER, :].astype(np.float32)
    artists = data["artists"]

    center = vectors.mean(axis=0)
    centered = vectors - center
    centered /= np.linalg.norm(centered, axis=1, keepdims=True) + 1e-9

    names = sorted(set(artists.tolist()))
    lookup = {name: i for i, name in enumerate(names)}
    ids = np.array([lookup[a] for a in artists], dtype=np.int32)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    with out_path.open("wb") as handle:
        handle.write(struct.pack("<ii", centered.shape[0], centered.shape[1]))
        handle.write(center.astype(np.float32).tobytes(order="C"))
        handle.write(centered.astype(np.float32).tobytes(order="C"))
        handle.write(struct.pack("<i", len(names)))
        for name in names:
            encoded = name.encode("utf-8")
            handle.write(struct.pack("<i", len(encoded)))
            handle.write(encoded)
        handle.write(ids.tobytes(order="C"))

    print(f"индекс: {out_path.name}, {out_path.stat().st_size / 1e6:.1f} МБ, "
          f"{centered.shape[0]} треков, {len(names)} артистов")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--embeddings", default="data/embeddings_mert.npz")
    parser.add_argument("--manifest", default="data/manifest24.csv")
    parser.add_argument("--models-dir", default="../MessedUpSearchA/Assets/Models")
    args = parser.parse_args()

    base = Path(__file__).resolve().parent.parent
    models_dir = (base / args.models_dir).resolve()

    model = build_model()

    # Модель (208 МБ) не влезает в git — лимит GitHub 100 МБ на файл. Она едет
    # отдельным файлом в релиз, приложение качает её при первом анализе.
    # В репозитории лежит только индекс — он маленький.
    dist_dir = (base / "dist").resolve()
    dist_dir.mkdir(parents=True, exist_ok=True)
    model_path = dist_dir / "mert_layer5.onnx"

    export(model, model_path)

    with (base / args.manifest).open(encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))
    sample = [rows[i]["path"] for i in range(0, len(rows), max(1, len(rows) // 12))][:12]

    verify(model, model_path, sample)
    write_index((base / args.embeddings).resolve(), models_dir / "artist_index.bin")

    print(f"\nмодель для релиза: {model_path} "
          f"({model_path.stat().st_size / 1e6:.0f} МБ)")
    print(f"индекс в репозитории: {models_dir / 'artist_index.bin'}")


if __name__ == "__main__":
    main()

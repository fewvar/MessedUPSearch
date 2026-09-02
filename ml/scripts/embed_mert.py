"""
Эмбеддинги MERT (m-a-p/MERT-v1-*) — модель, обученная на музыке, в отличие от
PANNs, которая училась на звуках вообще (лай, сирены, речь).

У трансформеров вроде MERT разные слои отвечают за разное: нижние держат тембр и
локальную фактуру, верхние — более абстрактную структуру. Какой слой лучше именно
для «похожести артиста», заранее не известно, поэтому сохраняем ВСЕ слои и выбираем
победителя по метрике в evaluate_mert.py, а не на глаз.

Вход модели — 24 кГц моно.
"""
import argparse
import csv
import sys
import time
from pathlib import Path

import numpy as np
import soundfile as sf
import torch

SAMPLE_RATE = 24000
WINDOW_SECONDS = 10
MAX_WINDOWS = 10
MIN_SECONDS = 3


def load_windows(path: str) -> np.ndarray | None:
    try:
        audio, sr = sf.read(path, dtype="float32", always_2d=False)
    except Exception:
        return None

    if audio.ndim > 1:
        audio = audio.mean(axis=1)
    if sr != SAMPLE_RATE or audio.size < MIN_SECONDS * SAMPLE_RATE:
        return None

    window = WINDOW_SECONDS * SAMPLE_RATE
    if audio.size < window:
        return np.pad(audio, (0, window - audio.size))[None, :]

    count = min(MAX_WINDOWS, audio.size // window)
    starts = np.linspace(0, audio.size - window, count).astype(int)
    return np.stack([audio[s:s + window] for s in starts])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", default="m-a-p/MERT-v1-95M")
    parser.add_argument("--manifest", default="data/manifest24.csv")
    parser.add_argument("--out", default="data/embeddings_mert.npz")
    parser.add_argument("--batch", type=int, default=4)
    parser.add_argument("--limit", type=int, default=0, help="0 — все треки")
    parser.add_argument("--checkpoint-every", type=int, default=150,
                        help="как часто сбрасывать промежуточный результат на диск")
    args = parser.parse_args()

    from transformers import AutoModel

    base = Path(__file__).resolve().parent.parent
    with (base / args.manifest).open(encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))
    if args.limit:
        rows = rows[:args.limit]
    print(f"треков: {len(rows)}")

    device = "mps" if torch.backends.mps.is_available() else "cpu"
    print(f"устройство: {device}, модель: {args.model}")

    model = AutoModel.from_pretrained(args.model, trust_remote_code=True).eval().to(device)

    # output_hidden_states у MERT не работает: его кастомный код писался под
    # transformers 4.x и в 5.x возвращает None. Снимаем активации хуками —
    # это не зависит от API модели вообще. Слой отдаёт голый тензор, а не
    # кортеж, поэтому проверяем тип, иначе из батча уцелел бы первый элемент.
    captured = {}

    def make_hook(layer_index):
        def hook(module, inputs, output):
            tensor = output[0] if isinstance(output, tuple) else output
            captured[layer_index] = tensor.detach()
        return hook

    handles = [layer.register_forward_hook(make_hook(i))
               for i, layer in enumerate(model.encoder.layers)]

    n_layers = len(model.encoder.layers)
    dim = model.config.hidden_size
    print(f"слоёв: {n_layers}, размерность: {dim}")

    vectors, artists, albums, titles, skipped = [], [], [], [], []
    started = time.time()

    # Прогон идёт десятки минут и его может оборвать что угодно. Пишем
    # промежуточный результат на диск и при следующем запуске продолжаем
    # с того же места, а не считаем всё заново.
    out_path = (base / args.out).resolve()
    checkpoint_path = out_path.with_suffix(".part.npz")
    done_titles = set()

    if checkpoint_path.exists():
        saved = np.load(checkpoint_path, allow_pickle=True)
        vectors = list(saved["embeddings"])
        artists = list(saved["artists"])
        albums = list(saved["albums"])
        titles = list(saved["titles"])
        done_titles = {(a, t) for a, t in zip(artists, titles)}
        print(f"продолжаю с чекпоинта: уже посчитано {len(vectors)}")

    def save(path, final=False):
        if not vectors:
            return
        np.savez_compressed(path, embeddings=np.stack(vectors),
                            artists=np.array(artists), albums=np.array(albums),
                            titles=np.array(titles))
        if not final:
            print(f"  [чекпоинт: {len(vectors)}]", flush=True)

    with torch.no_grad():
        for index, row in enumerate(rows, 1):
            if (row["artist"], row["title"]) in done_titles:
                continue

            windows = load_windows(row["path"])
            if windows is None:
                skipped.append(f"{row['artist']}/{row['title']}")
                continue

            per_window = []
            for start in range(0, len(windows), args.batch):
                batch = torch.from_numpy(windows[start:start + args.batch]).to(device)
                # Нормализация входа — так же, как это делает Wav2Vec2FeatureExtractor.
                batch = (batch - batch.mean(dim=1, keepdim=True)) / (batch.std(dim=1, keepdim=True) + 1e-7)

                captured.clear()
                model(batch)
                # (слои, батч, время, dim) -> усредняем по времени
                stacked = torch.stack([captured[i] for i in range(n_layers)], dim=0).mean(dim=2)
                per_window.append(stacked.permute(1, 0, 2).float().cpu().numpy())

            track = np.concatenate(per_window, axis=0).mean(axis=0)  # (слои, dim)
            if not np.isfinite(track).all():
                skipped.append(f"{row['artist']}/{row['title']} (не-конечные значения)")
                continue

            vectors.append(track.astype(np.float32))
            artists.append(row["artist"])
            albums.append(row["album"])
            titles.append(row["title"])

            if index % 25 == 0 or index == len(rows):
                speed = max(1, len(vectors) - len(done_titles)) / (time.time() - started)
                print(f"  {index}/{len(rows)}  {speed:.1f} трек/с  "
                      f"осталось ~{(len(rows) - index) / speed / 60:.1f} мин", flush=True)

            if len(vectors) % args.checkpoint_every == 0 and len(vectors) > 0:
                save(checkpoint_path)

    for handle in handles:
        handle.remove()

    matrix = np.stack(vectors)  # (N, слои, dim)
    save(out_path, final=True)
    checkpoint_path.unlink(missing_ok=True)

    print(f"\nготово за {(time.time() - started) / 60:.1f} мин")
    print(f"матрица: {matrix.shape} (треки, слои, размерность) -> {out_path}")
    if skipped:
        print(f"пропущено {len(skipped)}: {skipped[:5]}")


if __name__ == "__main__":
    main()

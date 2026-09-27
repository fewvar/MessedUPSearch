"""
Вторая половина конвейера краулера (первая — `crawler listen --slices-only`).

Забирает куски треков из папки-очереди (ml/data/slices/<track_id>.mp3), для каждого:
  1. Demucs htdemucs отделяет вокал (модель загружена один раз, MPS);
  2. считает два вектора MERT слоя 5 — ровно как ONNX в приложении: 24 кГц моно,
     до 6 окон по 10 с, нормализация каждого окна, среднее по времени и окнам:
       'full' — кусок как есть, с голосом: по нему фильтр «рэпер или нет»;
       'inst' — только инструментал: по нему поиск «бит -> артист»
       (замер: +8.8 п.п. top-5 против треков с голосом, см. ml/README.md);
  3. пишет оба в crawl.db (таблица embeddings), трек -> 'embedded';
  4. удаляет mp3 сразу — звук на диске не копится.

Работает, пока C# качает: ждёт новые файлы; когда C# кладёт метку .done и очередь пуста — выходит.

  python separate_embed.py                  # ждать очередь по умолчанию
  python separate_embed.py --check a.mp3    # один файл: напечатать full-вектор (сверка с C#)
"""
import argparse
import sqlite3
import time
from pathlib import Path

import numpy as np
import torch
import torchaudio

BASE = Path(__file__).resolve().parent.parent
SAMPLE_RATE = 24000
WINDOW = 10 * SAMPLE_RATE
MAX_WINDOWS = 6          # как MertEmbedder в приложении
MIN_SECONDS = 15         # как TrackEmbeddingQueue: короче — сниппет, вектор случайный
# Demucs — самое дорогое место (~12 с на 60 с звука на M4). Берём центральные 30 с куска:
# замер показал, что 30-секундные превью Deezer не хуже полных треков, а время вдвое меньше.
MAX_SECONDS = 30
LAYER = 5

DEVICE = "mps" if torch.backends.mps.is_available() else "cpu"


def load_mert():
    from transformers import AutoModel

    model = AutoModel.from_pretrained("m-a-p/MERT-v1-95M", trust_remote_code=True).eval()
    # Как в export_mert_onnx.py: слои выше пятого не нужны — дешевле и тот же выход.
    model.encoder.layers = torch.nn.ModuleList(list(model.encoder.layers)[:LAYER + 1])
    return model.to(DEVICE)


def load_demucs():
    from demucs.pretrained import get_model

    model = get_model("htdemucs").eval()
    return model.to(DEVICE)


def windows_of(mono24):
    """Окна как в MertEmbedder.SliceWindows: равномерно по всему куску."""
    n = mono24.shape[0]
    if n < 3 * SAMPLE_RATE:
        return None
    if n <= WINDOW:
        return torch.nn.functional.pad(mono24, (0, WINDOW - n))[None, :]
    count = min(MAX_WINDOWS, n // WINDOW)
    starts = np.linspace(0, n - WINDOW, count).astype(int) if count > 1 else [0]
    return torch.stack([mono24[s:s + WINDOW] for s in starts])


@torch.no_grad()
def embed(mert, mono24):
    batch = windows_of(mono24)
    if batch is None:
        return None
    batch = batch.to(DEVICE)
    batch = (batch - batch.mean(dim=1, keepdim=True)) / (batch.std(dim=1, keepdim=True) + 1e-7)
    hidden = mert(batch).last_hidden_state          # (окна, время, 768) — это выход слоя 5
    return hidden.mean(dim=1).mean(dim=0).float().cpu().numpy()


@torch.no_grad()
def separate(demucs, wav):
    """wav: (каналы, сэмплы) на частоте Demucs -> инструментал той же формы."""
    from demucs.apply import apply_model

    ref = wav.mean(0)
    mean, std = ref.mean(), ref.std() + 1e-8
    sources = apply_model(demucs, ((wav - mean) / std)[None].to(DEVICE),
                          device=DEVICE, split=True, overlap=0.25, progress=False)[0]
    sources = sources * std + mean
    keep = [i for i, name in enumerate(demucs.sources) if name != "vocals"]
    return sources[keep].sum(0).cpu()


def to_mono24(wav, rate):
    return torchaudio.functional.resample(wav.mean(0), rate, SAMPLE_RATE)


def read_audio(path, demucs):
    from demucs.audio import AudioFile

    wav = AudioFile(path).read(streams=0, samplerate=demucs.samplerate, channels=demucs.audio_channels)
    return wav


def process(path, mert, demucs, db):
    track_id = int(path.stem)
    wav = read_audio(path, demucs)
    keep = MAX_SECONDS * demucs.samplerate
    if wav.shape[-1] > keep:
        start = (wav.shape[-1] - keep) // 2
        wav = wav[:, start:start + keep]
    seconds = wav.shape[-1] / demucs.samplerate

    if seconds < MIN_SECONDS:
        db.execute("UPDATE tracks SET status='failed', error=? WHERE id=?", (f"короче {MIN_SECONDS} с", track_id))
        return "short"

    full = embed(mert, to_mono24(wav, demucs.samplerate))
    inst = embed(mert, to_mono24(separate(demucs, wav), demucs.samplerate))

    if full is None or inst is None or not (np.isfinite(full).all() and np.isfinite(inst).all()):
        db.execute("UPDATE tracks SET status='failed', error='не посчитался вектор' WHERE id=?", (track_id,))
        return "bad"

    for kind, vector in (("full", full), ("inst", inst)):
        db.execute("INSERT OR REPLACE INTO embeddings (track_id, kind, vector, seconds) VALUES (?, ?, ?, ?)",
                   (track_id, kind, vector.astype(np.float32).tobytes(), round(seconds, 1)))
    db.execute("UPDATE tracks SET status='embedded', error='' WHERE id=?", (track_id,))
    return "ok"


def run(queue, db_path):
    mert, demucs = load_mert(), load_demucs()
    db = sqlite3.connect(db_path, timeout=30, isolation_level=None)
    db.execute("PRAGMA journal_mode=WAL")
    print(f"устройство {DEVICE}; жду куски в {queue}", flush=True)

    started, stats = time.time(), {"ok": 0, "short": 0, "bad": 0, "error": 0}
    while True:
        files = sorted(queue.glob("*.mp3"), key=lambda p: p.stat().st_mtime)
        if not files:
            if (queue / ".done").exists():
                break
            time.sleep(2)
            continue

        for path in files:
            try:
                stats[process(path, mert, demucs, db)] += 1
            except Exception as ex:   # один битый файл не должен ронять ночной прогон
                stats["error"] += 1
                db.execute("UPDATE tracks SET status='failed', error=? WHERE id=?", (str(ex)[:300], int(path.stem)))
            finally:
                path.unlink(missing_ok=True)

            done = sum(stats.values())
            print(f"\r{done} кусков  {stats}  {(time.time() - started) / done:.1f} с/кусок   ", end="", flush=True)

    total = sum(stats.values())
    print(f"\nготово: {stats}, {(time.time() - started) / max(1, total):.1f} с на кусок")


def check(paths):
    """Файлы -> full-векторы в <файл>.py.f32 рядом: для сверки с C# (MlCheck raw)."""
    mert, demucs = load_mert(), load_demucs()
    for path in map(Path, paths):
        vector = embed(mert, to_mono24(read_audio(path, demucs), demucs.samplerate))
        vector.astype(np.float32).tofile(path.with_suffix(".py.f32"))
    print(f"посчитано: {len(paths)}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--queue", default=str(BASE / "data/slices"))
    parser.add_argument("--db", default=str(BASE / "data/crawl.db"))
    parser.add_argument("--check", nargs="*")
    args = parser.parse_args()

    if args.check:
        check(args.check)
    else:
        queue = Path(args.queue)
        queue.mkdir(parents=True, exist_ok=True)
        run(queue, args.db)

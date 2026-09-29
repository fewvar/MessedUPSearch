"""
Demucs один раз — инструменталы на диск (tmp/plans/models-v0.7.md).

Раньше separate_embed.py считал вектор MERT и удалял звук, поэтому любая другая модель требовала
ночи скачивания и ночи Demucs. Здесь инструментал центральных 30 с каждого куска сохраняется
в data/inst/<группа>/<id>.mp3 (моно 44.1 кГц, 192k) — дальше любая модель считается за часы.

Группы:
  underground  data/audio/*.mp3            (crawler reslice, 652 артиста индекса и отсеянные)
  refs_sc      data/audio_refs/*.mp3       (crawler reslice --db crawl_refs.db)
  refs_deezer  data/vocal_test/previews/<артист>/*.mp3

Исходники не трогаются. Готовое пропускается — можно прерывать и перезапускать.
--follow: ждать новые файлы, пока идёт reslice (выход, когда процесса reslice нет и всё готово).

  nice -n 10 python inst_cache.py --follow
"""
import argparse
import subprocess
import time
from pathlib import Path

import torch

from separate_embed import MAX_SECONDS, load_demucs, read_audio, separate

BASE = Path(__file__).resolve().parent.parent
OUT = BASE / "data/inst"
PAUSE = 0.5          # пауза между кусками: видеокарта не занята на 100% (режим ~90%)
MIN_SECONDS = 15


def sources():
    yield from (("underground", p.stem, p) for p in sorted((BASE / "data/audio").glob("*.mp3")))
    yield from (("refs_sc", p.stem, p) for p in sorted((BASE / "data/audio_refs").glob("*.mp3")))
    for p in sorted((BASE / "data/vocal_test/previews").glob("*/*.mp3")):
        yield "refs_deezer", f"{p.parent.name}__{p.stem}", p


def save_mp3(wav, rate, path):
    """(каналы, сэмплы) float -> моно mp3 через ffmpeg (атомарно: .part -> .mp3)."""
    mono = wav.mean(0).clamp(-1, 1).numpy().astype("float32")
    part = path.with_suffix(".part")
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ar", str(rate), "-ac", "1", "-i", "-",
                    "-b:a", "192k", "-f", "mp3", str(part)], input=mono.tobytes(), check=True)
    part.rename(path)


def process(demucs, src, dst):
    wav = read_audio(src, demucs)
    keep = MAX_SECONDS * demucs.samplerate
    if wav.shape[-1] > keep:
        start = (wav.shape[-1] - keep) // 2
        wav = wav[:, start:start + keep]
    if wav.shape[-1] < MIN_SECONDS * demucs.samplerate:
        return "short"
    save_mp3(separate(demucs, wav), demucs.samplerate, dst)
    return "ok"


def reslice_running():
    return subprocess.run(["pgrep", "-f", "Crawler.* reslice"], capture_output=True).returncode == 0


def run(follow):
    demucs = load_demucs()
    stats, started = {"ok": 0, "short": 0, "error": 0}, time.time()

    while True:
        todo = [(g, i, p) for g, i, p in sources() if not (OUT / g / f"{i}.mp3").exists()
                and not (OUT / g / f"{i}.skip").exists()]
        # Файл, который reslice ещё пишет, лежит как временный — в data/audio попадает целиком (File.Move).
        if not todo:
            if follow and reslice_running():
                time.sleep(20)
                continue
            break

        for group, key, src in todo:
            dst = OUT / group / f"{key}.mp3"
            dst.parent.mkdir(parents=True, exist_ok=True)
            try:
                result = process(demucs, src, dst)
                if result == "short":
                    dst.with_suffix(".skip").write_text("short")
                stats[result] += 1
            except Exception as ex:   # один битый файл не роняет ночь
                stats["error"] += 1
                dst.with_suffix(".skip").write_text(str(ex)[:300])
            done = sum(stats.values())
            print(f"\r{done} кусков  {stats}  {(time.time() - started) / done:.1f} с/кусок   ", end="", flush=True)
            time.sleep(PAUSE)

    print(f"\nготово: {stats}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--follow", action="store_true")
    args = parser.parse_args()
    torch.set_num_threads(max(1, (torch.get_num_threads() * 3) // 4))
    run(args.follow)

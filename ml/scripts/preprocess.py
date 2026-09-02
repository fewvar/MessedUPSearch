"""
mp3 из архива -> моно WAV 32 кГц (частота, на которой обучен PANNs) + manifest.csv.

Конвертация идёт через ffmpeg в 10 процессов. Битый файл не роняет прогон:
он попадает в отчёт, а остальные продолжают конвертироваться.
"""
import argparse
import csv
import subprocess
import sys
from collections import Counter
from concurrent.futures import ProcessPoolExecutor, as_completed
from pathlib import Path

MIN_TRACKS_PER_ARTIST = 10  # меньше — статистики не будет, артист отсеивается


def convert(job):
    """Один mp3 -> wav. Возвращает (успех, запись для манифеста, сообщение об ошибке)."""
    src, dst, artist, album, title, sample_rate = job
    dst.parent.mkdir(parents=True, exist_ok=True)

    if dst.exists() and dst.stat().st_size > 1024:
        return True, (str(dst), artist, album, title), None

    result = subprocess.run(
        ["ffmpeg", "-nostdin", "-y", "-loglevel", "error",
         "-i", str(src), "-ac", "1", "-ar", str(sample_rate), str(dst)],
        capture_output=True, text=True)

    if result.returncode != 0 or not dst.exists() or dst.stat().st_size <= 1024:
        dst.unlink(missing_ok=True)
        error = (result.stderr or "пустой файл на выходе").strip().splitlines()
        return False, None, f"{artist}/{title}: {error[-1] if error else 'ffmpeg упал'}"

    return True, (str(dst), artist, album, title), None


def collect_jobs(raw_dir: Path, out_dir: Path, sample_rate: int):
    """Обходит ARTIST'S/<артист>/<альбом>/*.mp3 и решает, кого вообще брать."""
    root = next((p for p in raw_dir.iterdir() if p.is_dir()), None)
    if root is None:
        sys.exit(f"в {raw_dir} нет распакованной папки")

    by_artist = {}
    for mp3 in sorted(root.rglob("*.mp3")):
        rel = mp3.relative_to(root).parts
        if len(rel) < 2:
            continue  # трек лежит прямо в папке артиста, без альбома — пропускаем
        artist, album = rel[0], rel[1]
        by_artist.setdefault(artist, []).append((mp3, album, mp3.stem))

    dropped = {a: len(t) for a, t in by_artist.items() if len(t) < MIN_TRACKS_PER_ARTIST}

    jobs = []
    for artist, tracks in by_artist.items():
        if artist in dropped:
            continue
        for index, (mp3, album, title) in enumerate(tracks):
            dst = out_dir / artist / f"{index:04d}.wav"
            jobs.append((mp3, dst, artist, album, title, sample_rate))

    return jobs, dropped


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw", default="data/raw")
    parser.add_argument("--out", default="data/audio")
    parser.add_argument("--manifest", default="data/manifest.csv")
    parser.add_argument("--workers", type=int, default=10)
    parser.add_argument("--sample-rate", type=int, default=32000,
                        help="32000 для PANNs, 24000 для MERT")
    args = parser.parse_args()

    base = Path(__file__).resolve().parent.parent
    raw_dir = (base / args.raw).resolve()
    out_dir = (base / args.out).resolve()
    manifest_path = (base / args.manifest).resolve()

    jobs, dropped = collect_jobs(raw_dir, out_dir, args.sample_rate)
    print(f"к конвертации: {len(jobs)} треков @ {args.sample_rate} Гц")
    if dropped:
        print("отсеяны артисты (мало треков):",
              ", ".join(f"{a} ({n})" for a, n in sorted(dropped.items())))

    rows, failures = [], []
    with ProcessPoolExecutor(max_workers=args.workers) as pool:
        futures = [pool.submit(convert, job) for job in jobs]
        for done, future in enumerate(as_completed(futures), 1):
            ok, row, error = future.result()
            if ok:
                rows.append(row)
            else:
                failures.append(error)
            if done % 100 == 0 or done == len(futures):
                print(f"  {done}/{len(futures)}", flush=True)

    rows.sort()
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    with manifest_path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["path", "artist", "album", "title"])
        writer.writerows(rows)

    per_artist = Counter(row[1] for row in rows)
    print(f"\nготово: {len(rows)} треков, {len(per_artist)} артистов -> {manifest_path}")
    print("треков на артиста: min %d, медиана %d, max %d" % (
        min(per_artist.values()),
        sorted(per_artist.values())[len(per_artist) // 2],
        max(per_artist.values())))

    if failures:
        print(f"\nне сконвертировались ({len(failures)}):")
        for line in failures[:20]:
            print("  ", line)
        if len(failures) > 20:
            print(f"   ... и ещё {len(failures) - 20}")


if __name__ == "__main__":
    main()

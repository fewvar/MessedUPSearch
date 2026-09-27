"""
Шаг 2 плана качества: сравнивать бит с треком целиком или только с его инструменталом?

База для сравнения — превью Deezer (30 с) тех же 32 артистов, что в линейке type beat'ов:
легально, мало весит, и замер показал, что превью не хуже полных треков. Вокал
отделяется Demucs (htdemucs, MIT). На выходе три манифеста для embed_mert.py:
  full  — превью как есть (с голосом) — как сейчас в индексе;
  inst  — только инструментал (no_vocals);
Третий вариант (среднее векторов full и inst) собирает evaluate_vocal.py.

Этапы идут по флагам, чтобы тяжёлое (Demucs на видеокарте) запускать отдельно:
  --download   скачать превью (лёгкое, сеть)
  --separate   Demucs (тяжёлое, видеокарта)
  --manifests  ffmpeg в 24 кГц моно + манифесты
"""
import argparse
import csv
import json
import re
import struct
import subprocess
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

BASE = Path(__file__).resolve().parent.parent
PREVIEWS = BASE / "data/vocal_test/previews"
SEPARATED = BASE / "data/vocal_test/demucs"
WAV = BASE / "data/vocal_test/wav24"
PER_ARTIST = 10


def names_from_index():
    with open(BASE.parent / "MessedUpSearchA/Assets/Models/artist_index.bin", "rb") as f:
        count, dim = struct.unpack("<ii", f.read(8))
        f.seek(4 * dim * (count + 1), 1)
        artists = struct.unpack("<i", f.read(4))[0]
        names = []
        for _ in range(artists):
            length = struct.unpack("<i", f.read(4))[0]
            names.append(f.read(length).decode("utf-8"))
    return names


def aliases():
    path = BASE / "scripts/deezer_aliases.tsv"
    return dict(line.split("\t") for line in path.read_text().splitlines() if "\t" in line)


def norm(text):
    return re.sub(r"[^\w]", "", text.lower())


def get(url):
    time.sleep(0.15)   # Deezer: 50 запросов за 5 секунд
    with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"}), timeout=30) as r:
        return r.read()


def download():
    alias = aliases()
    for name in names_from_index():
        folder = PREVIEWS / re.sub(r"[^\w]", "_", name)
        if folder.exists() and len(list(folder.glob("*.mp3"))) >= PER_ARTIST:
            continue
        query = alias.get(name, name)
        found = json.loads(get("https://api.deezer.com/search/artist?q=" + urllib.parse.quote(query)))["data"]
        exact = [a for a in found if norm(a["name"]) == norm(query)]
        if not exact:
            print(f"{name}: на Deezer не нашёлся")
            continue
        artist = max(exact, key=lambda a: a["nb_fan"])
        top = json.loads(get(f"https://api.deezer.com/artist/{artist['id']}/top?limit={PER_ARTIST}"))["data"]
        folder.mkdir(parents=True, exist_ok=True)
        got = 0
        for track in top:
            if not track.get("preview"):
                continue
            target = folder / f"{track['id']}.mp3"
            if not target.exists():
                target.write_bytes(get(track["preview"]))
            got += 1
        print(f"{name}: {got} превью")


def separate():
    files = sorted(str(p) for p in PREVIEWS.glob("*/*.mp3")
                   if not (SEPARATED / "htdemucs" / p.stem / "no_vocals.wav").exists())
    print(f"к разделению: {len(files)}")
    for start in range(0, len(files), 20):
        subprocess.run([sys.executable, "-m", "demucs", "--two-stems", "vocals", "-n", "htdemucs",
                        "-d", "mps", "-o", str(SEPARATED), *files[start:start + 20]], check=True)


def manifests():
    WAV.mkdir(parents=True, exist_ok=True)
    rows = {"full": [], "inst": []}
    for mp3 in sorted(PREVIEWS.glob("*/*.mp3")):
        artist_folder = mp3.parent.name
        inst = SEPARATED / "htdemucs" / mp3.stem / "no_vocals.wav"
        if not inst.exists():
            continue
        for kind, source in (("full", mp3), ("inst", inst)):
            wav = WAV / f"{mp3.stem}_{kind}.wav"
            if not wav.exists():
                subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(source), "-ac", "1", "-ar", "24000", str(wav)], check=True)
            rows[kind].append({"path": str(wav), "artist": artist_folder, "album": mp3.stem, "title": mp3.stem})

    # Имена папок — «безопасные» версии ников; возвращаем настоящие ники из индекса.
    real = {re.sub(r"[^\w]", "_", n): n for n in names_from_index()}
    for kind, items in rows.items():
        for item in items:
            item["artist"] = real.get(item["artist"], item["artist"])
        with (BASE / f"data/manifest_vocal_{kind}.csv").open("w", encoding="utf-8", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=["path", "artist", "album", "title"])
            writer.writeheader()
            writer.writerows(items)
        print(f"{kind}: {len(items)}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--download", action="store_true")
    parser.add_argument("--separate", action="store_true")
    parser.add_argument("--manifests", action="store_true")
    args = parser.parse_args()
    if args.download:
        download()
    if args.separate:
        separate()
    if args.manifests:
        manifests()

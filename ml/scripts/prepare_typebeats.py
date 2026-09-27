"""
Куски type beat'ов (mp3 из crawler typebeats) -> моно WAV 24 кГц + манифест для
embed_mert.py. Та же подготовка, что у треков друга (preprocess.py), чтобы запросы
и база считались одинаково.
"""
import csv
import subprocess
from pathlib import Path

BASE = Path(__file__).resolve().parent.parent
SRC = BASE / "data/typebeats/typebeats.csv"
OUT = BASE / "data/typebeats24"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    rows = list(csv.DictReader(SRC.open(encoding="utf-8-sig")))  # C# пишет UTF-8 с BOM

    manifest = []
    for row in rows:
        wav = OUT / f"{row['track_id']}.wav"
        if not wav.exists():
            result = subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", row["path"], "-ac", "1", "-ar", "24000", str(wav)],
                                    capture_output=True, text=True)
            if result.returncode != 0:
                print(f"пропуск {row['track_id']}: {result.stderr.strip()[:120]}")
                continue
        # album = битмейкер: у одного автора звук похожий, это не должно считаться «разными битами».
        manifest.append({"path": str(wav), "artist": row["artist"], "album": row["uploader"], "title": row["title"]})

    with (BASE / "data/manifest_typebeats.csv").open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=["path", "artist", "album", "title"])
        writer.writeheader()
        writer.writerows(manifest)

    print(f"битов: {len(manifest)} из {len(rows)}")


if __name__ == "__main__":
    main()

#!/bin/bash
# Убирает промежуточные данные, когда они уже не нужны.
# Порядок важен: сначала то, что дёшево воссоздать, и только потом исходники.
set -u
ML="/Users/vasiliy/RiderProjects/MessedUpSearchApp/ml"
MODE="${1:-wav}"

size() { du -sh "$1" 2>/dev/null | cut -f1; }

case "$MODE" in
  wav)
    # WAV нужны только для расчёта эмбеддингов. Есть .npz — можно сносить:
    # воссоздаются из mp3 за пару минут препроцессингом.
    [ -f "$ML/data/embeddings_mert.npz" ] || { echo "нет embeddings_mert.npz — не чищу"; exit 1; }
    for dir in "$ML/data/audio" "$ML/data/audio24"; do
        [ -d "$dir" ] && echo "удаляю $dir ($(size "$dir"))" && rm -rf "$dir"
    done
    ;;
  mp3)
    # Распакованные mp3. Нужны, если решим переделать препроцессинг
    # (например, прогнать Demucs). Архив при этом остаётся.
    [ -f "$ML/data/raw/artists.zip" ] || { echo "нет архива — не чищу mp3"; exit 1; }
    echo "удаляю распакованные mp3 ($(size "$ML/data/raw/ARTIST'S"))"
    rm -rf "$ML/data/raw/ARTIST'S"
    ;;
  archive)
    # Сам архив. Только когда всё готово — иначе перекачивать 9.8 ГБ час.
    echo "удаляю архив ($(size "$ML/data/raw/artists.zip"))"
    rm -f "$ML/data/raw/artists.zip"
    ;;
  *)
    echo "использование: cleanup.sh wav|mp3|archive"; exit 1;;
esac

echo "свободно на диске: $(df -k /System/Volumes/Data | tail -1 | awk '{printf "%.0f ГБ", $4/1048576}')"

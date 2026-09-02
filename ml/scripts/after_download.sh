#!/bin/bash
# Ждёт докачки архива, распаковывает и сразу запускает препроцессинг.
set -u
ML="/Users/vasiliy/RiderProjects/MessedUpSearchApp/ml"
ZIP="$ML/data/raw/artists.zip"
EXPECTED=10536888109

echo "жду докачки..."
while true; do
    have=$(stat -f%z "$ZIP" 2>/dev/null || echo 0)
    [ "$have" -eq "$EXPECTED" ] && break
    # если curl умер, а размер не растёт — выходим, а не ждём вечно
    if ! pgrep -f "artists.zip" >/dev/null 2>&1; then
        sleep 20
        again=$(stat -f%z "$ZIP" 2>/dev/null || echo 0)
        if [ "$again" -eq "$have" ] && [ "$again" -ne "$EXPECTED" ]; then
            echo "ОШИБКА: загрузка встала на $have из $EXPECTED"; exit 1
        fi
    fi
    sleep 15
done
echo "архив полный: $EXPECTED байт"

echo "распаковываю..."
cd "$ML/data/raw" || exit 1
# Системный unzip 6.00 (2009) виснет на zip64 больше 4 ГБ — проверено на этом
# самом архиве: полчаса, 15 секунд CPU, 468 файлов из 1689 и стоп.
bsdtar -xf artists.zip --exclude '__MACOSX/*' 2>&1 | tail -5
mp3s=$(find "$ML/data/raw" -name '*.mp3' -not -path '*__MACOSX*' | wc -l | tr -d ' ')
echo "распаковано mp3: $mp3s (ожидалось 1689)"
[ "$mp3s" -lt 1600 ] && { echo "ОШИБКА: файлов меньше ожидаемого"; exit 1; }

echo "препроцессинг..."
cd "$ML" || exit 1
.venv/bin/python scripts/preprocess.py 2>&1 | tail -40
echo "ЦЕПОЧКА ЗАВЕРШЕНА"

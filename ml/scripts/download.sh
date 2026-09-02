#!/bin/bash
# Качает artists.zip с Google Drive. Поддерживает докачку: прерванный
# запуск продолжается с того же места, а не начинается заново.
set -u
FILE_ID="1XP01NHJlshjbQtWb_hy9-XSGA50t6FGU"
DEST="$(cd "$(dirname "$0")/.." && pwd)/data/raw/artists.zip"
EXPECTED=10536888109
COOKIE=$(mktemp)

if [ -f "$DEST" ]; then
    have=$(stat -f%z "$DEST")
    if [ "$have" -eq "$EXPECTED" ]; then
        echo "уже скачан целиком: $DEST"; exit 0
    fi
    echo "докачиваю с $have из $EXPECTED байт"
fi

# Для больших файлов Drive сначала отдаёт страницу подтверждения с одноразовым uuid.
UUID=$(curl -sL --max-time 60 -c "$COOKIE" \
    "https://drive.usercontent.google.com/download?id=${FILE_ID}&export=download" \
    | grep -oE 'name="uuid" value="[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$UUID" ]; then echo "не получил uuid подтверждения"; exit 1; fi
echo "uuid: $UUID"

curl -L --fail --retry 5 --retry-delay 10 --retry-all-errors -C - \
     -b "$COOKIE" \
     "https://drive.usercontent.google.com/download?id=${FILE_ID}&export=download&confirm=t&uuid=${UUID}" \
     -o "$DEST"

rc=$?
rm -f "$COOKIE"
got=$(stat -f%z "$DEST" 2>/dev/null || echo 0)
echo "curl rc=$rc, размер: $got из $EXPECTED"
[ "$got" -eq "$EXPECTED" ] && echo "OK: скачан полностью" || { echo "НЕПОЛНЫЙ ФАЙЛ"; exit 1; }

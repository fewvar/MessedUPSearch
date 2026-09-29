#!/bin/zsh
# Большой прогон индекса доступных артистов — на ночь.
#   ml/scripts/night_run.sh [сколько рэперов, по умолчанию 2000]
#
# Что делает (всё продолжает с места после обрыва — можно перезапускать той же командой):
#   1. discover   — ищет рэперов по жанрам на SoundCloud/Audius (часы: лимит SoundCloud 60 запросов/мин)
#   2. listen     — C# качает по 4 куска на артиста в очередь data/slices
#      separate   — параллельно Python отделяет вокал и считает векторы (видеокарта); звук сохраняет
#                   в data/audio и data/inst (~1.4 МБ на кусок)
#   3. export     — собирает data/artists_index_v3.bin (EffNet + голова из Assets/Models/effnet_head.bin)
#
# caffeinate -i не даёт маку уснуть, пока идёт прогон. Мак горячий и занят — не для игр.
# Логи: data/night_*.log. Прервать: Ctrl+C (прогресс сохраняется в data/crawl.db).
set -e

TARGET=${1:-2000}
ML=${0:A:h:h}
TERMS="rage,plugg,jerk,cloud rap,phonk,dark trap,trap,sad rap,ambient rap,underground rap,emo rap,drill,rap,pluggnb,hyperpop,trap metal"

cd "$ML/crawler"
dotnet build -c Release -v q -nologo > /dev/null

caffeinate -i zsh -c "
  set -e
  echo '== поиск артистов'; date
  dotnet run -c Release --no-build -- discover --target $TARGET --terms '$TERMS' 2>&1 | tee '$ML/data/night_discover.log' | grep -a -E '^==|новых|набрано' || true

  echo '== прослушивание'; date
  ( cd '$ML' && nice -n 10 .venv/bin/python scripts/separate_embed.py > '$ML/data/night_separate.log' 2>&1 ) &
  SEPARATE=\$!
  dotnet run -c Release --no-build -- listen --slices-only --tracks 4 > '$ML/data/night_listen.log' 2>&1
  wait \$SEPARATE

  echo '== экспорт'; date
  dotnet run -c Release --no-build -- export
  dotnet run -c Release --no-build -- evaluate | tail -6
  date
"

# MessedUpSearch

Десктопная CRM и библиотека битов для музыкальных продюсеров. Держи свои биты в порядке, веди базу андеграунд-артистов, привязывай биты к тем, кому их питчишь, и получай напоминания, когда питч завис.

<!-- скриншот или GIF: <img src="docs/screenshot.png" width="100%"> -->

---

## Что умеет

Локальный инструмент, который превращает папку битов и список артистов в рабочий питч-пайплайн.

### Библиотека битов
- Добавление бита по одному через системный файловый пикер (WAV / MP3) с ручным вводом метаданных: название, BPM, тональность, жанровые теги.
- Массовый импорт целой папки разом — все WAV/MP3 заливаются одним действием, повторный импорт не плодит дубликаты (дедупликация по пути файла).
- Статус бита: «потенциал», «фри» или «продан» (для проданного указывается тип лицензии). Статус подсвечивается цветным маркером в таблице.
- Редактирование и удаление по клику на строку.
- Встроенный плеер: ▶ в строке бита, панель над строкой состояния с пиксельной волной (клик — перемотка), ⏮ ⏭ и автопереход по отфильтрованному списку, громкость запоминается. Пробел — пауза, ←/→ — ±5 секунд.
- В карточке артиста — его треки с площадок, их можно послушать тут же, не открывая SoundCloud.

### Артисты
- Ручное добавление: никнейм, ссылки (SoundCloud / Instagram / Spotify), язык, жанровые теги, прослушивания.
- Аватарки, скачанные парсером, видны в таблице и в карточке; у артистов без фото остаётся цветной кружок.
- Язык определяется по названиям треков и описанию профиля: кириллица, вьетнамские диакритики, кана, хангыль, иероглифы и арабица распознаются по письменности, европейские языки — по стоп-словам.
- Метки «избранное» и «ред-флаг» переключаются одним кликом прямо в таблице.
- CRM-статус (не ответил / ок / выложил фри) и свободные заметки.
- Дедупликация по ссылке на профиль: артистов без ссылки можно добавлять без ограничений, а у тех, где ссылка указана, она остаётся уникальной.

### Привязка битов к артистам
- Назначай один бит сразу нескольким артистам через чекбоксы в карточке бита.
- На карточке артиста виден список заготовленных для него битов.
- Связь хранит дату назначения и факт отправки — это основа для CRM и напоминаний.

### Фильтры и поиск
- Обе таблицы фильтруются вживую: по жанру, тональности (биты) / языку (артисты), диапазону BPM или прослушиваний, периоду и свободному тексту.
- Фильтры комбинируются и применяются мгновенно.

### CRM
- Отдельное окно для артистов, с которыми идёт работа (у кого проставлен CRM-статус).
- По каждому: статус, заметки и список назначенных битов с пометкой «ждёт отправки» / «отправлен».
- Кнопка «отметить отправленным» прямо в карточке.

### Интерфейс на двух языках
- Русский и английский переключаются в настройках и применяются сразу, без перезапуска. Выбор сохраняется между запусками.

### Напоминания
- Привязал бит, но так и не отправил? Через настраиваемое число дней приложение при запуске покажет напоминание, сгруппированное по артисту.
- Порог (1–14 дней) задаётся ползунком в настройках и сохраняется между запусками.
- Отметил бит отправленным — напоминание исчезает.


### Парсер артистов
- Ищет андеграунд-артистов сразу по пяти площадкам: Audius, SoundCloud, Bandcamp, Last.fm, Jamendo. Любую можно отключить галочкой.
- Критерии: жанр или тег, диапазон прослушиваний, свежесть релизов, страна, сколько артистов брать с каждой площадки.
- Собирает ник, ссылку на профиль, аватарку, прослушивания, дату последнего релиза, теги и треки; соцсети дополняются через Genius.
- Выдача приходит списком с галочками — в базу попадает только то, что ты отметил. Повторный прогон не плодит дубликаты и не трогает заметки, CRM-статус и флаги.
- Падение одной площадки не роняет прогон: остальные доработают, а в отчёте будет видно, кто отвалился и сколько успел собрать.
- Last.fm, Jamendo и Genius требуют бесплатных ключей — вписываются в настройках.

### Похожие артисты (по звучанию)
- Кнопка ANALYZE на карточке бита показывает, на кого он похож **по звучанию**, а не по тегам: `Osamason 87% · Summrs 81% · Autumn! 74%`.
- Под капотом — Discogs-EffNet (Essentia, MTG / Universitat Pompeu Fabra): нейросеть, обученная различать 400 музыкальных стилей Discogs. Она превращает бит в вектор из 1280 чисел; сверху — наша «голова», дообученная на 652 андеграунд-артистах так, чтобы треки одного артиста лежали рядом.
- Выдача из двух частей: «звучит как» (30 крупных артистов — ориентир стиля) и до 10 доступных андеграунд-артистов из индекса, собранного краулером. У каждого — ▶ послушать его трек, ➕ добавить в свою базу, ↗ открыть профиль.
- Бит сравнивается с ИНСТРУМЕНТАЛАМИ артистов (вокал отделяется при сборке индекса).
- Честная цифра на задаче продукта (type beat'ы, артист в обучении не участвовал): нужный артист в первой пятёрке среди 30 ориентиров в 53% случаев (случайно 17%); среди всех 682 артистов — в первой десятке в 13.5%. Модель выбрана замером из пяти (MERT, MuQ, CLAP, варианты EffNet) — подробности в `ml/README.md`.
- Модель (18 МБ) едет вместе с приложением — анализ работает сразу, без скачивания, около секунды на бит. Отдельно качается только индекс артистов (~6 МБ).
- Результат сохраняется в базу и не пересчитывается при каждом открытии карточки.
- Обратная сторона: в карточке артиста видно, какие из твоих битов ему подходят.
- Лицензия модели — CC BY-NC-SA 4.0: только некоммерческое использование.

Все данные, кроме скачивания индекса артистов, лежат в локальном файле SQLite на твоём компьютере — ничего никуда не загружается. Свежая установка стартует пустой, с приглушённым превью того, как выглядит заполненная таблица.

## Технологии

| Слой | Технология |
| :--- | :--- |
| UI | Avalonia 12 (XAML), FluentTheme, MVVM |
| Язык | C# / .NET 10 |
| MVVM | CommunityToolkit.Mvvm (генерация свойств и команд) |
| Данные | SQLite + Entity Framework Core 10 (code-first миграции) |
| Звук | SoundFlow (miniaudio) — воспроизведение |
| Настройки | локальный `settings.json` |

## Архитектура

```
App.axaml.cs                 применить миграции EF -> открыть MainWindow
   |
MainWindow (MainWindowViewModel)
   |-- вкладки: Beats / Artists  (переключение CurrentViewModel)
   |-- оверлеи: CRM / Settings / Reminders / Parser
   |
   +-- BeatsView   <- BeatsViewModel    (CRUD битов, фильтры, привязка артистов, похожесть)
   +-- ArtistsView <- ArtistsViewModel  (CRUD артистов, фильтры, флаги, живые ссылки)
   |
Services/Parsing              пять площадок за общим интерфейсом IArtistSource
   +-- ParserService          гоняет источники, собирает прогресс и отказы
   +-- Sources/               Audius, SoundCloud, Bandcamp, LastFm, Jamendo
   +-- GeniusLookup           соцсети и язык поверх найденного
   +-- ArtistImportService    upsert в базу, аватарки, треки
   |
Services/Ml                   похожесть бита на артистов
   +-- AudioDecoder           WAV/MP3 -> моно 16 кГц (свой windowed-sinc ресемплер, центральная минута)
   +-- EffNetEmbedder         звук -> мел Essentia -> EffNet (ONNX) -> голова; одна сессия на приложение
   +-- BeatSimilarityService  вектор бита -> «звучит как» + топ-10 доступных артистов
   +-- TargetIndex/IndexStore индексы v3: ориентиры (с приложением) и доступные артисты (качается)
   +-- HubCorrection          поправка на артистов, похожих на всё подряд
   +-- MlAssets               где лежат модель, голова и индексы
   |
Слой данных (EF Core)
   +-- AppDbContext --> SQLite app.db
        |-- Artists
        |-- ArtistTracks       (треки, найденные парсером)
        |-- TrackEmbeddings    (вектор звучания трека, 3 КБ; звук не хранится)
        |-- Beats
        |-- BeatSimilarities   (на кого похож бит: артист, ранг, проценты)
        +-- SentBeatsLog       (связи бит <-> артист: назначен / отправлен / дата)

ml/                          Python-кухня: датасет, эмбеддинги, метрики, экспорт в ONNX
                             (в работе приложения не участвует)
```

Расположение базы: `%AppData%/MessedUpSearch/app.db` (Windows), `~/Library/Application Support/MessedUpSearch/app.db` (macOS).

## Сборка и запуск

```bash
dotnet build
dotnet run --project MessedUpSearchA/MessedUpSearchA.csproj

# цикл разработки UI (горячая перезагрузка XAML + тел методов)
dotnet watch --project MessedUpSearchA/MessedUpSearchA.csproj

# релизная сборка
dotnet publish MessedUpSearchA/MessedUpSearchA.csproj -c Release
```

Кроссплатформенно: Windows и macOS.

## Планы (v2)

Намеренно вынесено за рамки текущей версии и запланировано следующим:

- **Жанровые ярлыки поверх похожести** — сгруппировать артистов в кластеры (rage, plugg, jerk) и показывать бит сразу тегом, а не только списком похожих.
- **Автотегирование BPM и тональности** — сейчас вводятся руками.
- **Определение языка по лирике** — сейчас язык угадывается по стране профиля и описанию с Genius, что работает так себе.
- **Email и Telegram-напоминания** — доставка напоминаний вне приложения, а не только при запуске.

## Статус

v0.5 — биты, артисты, связи, фильтры, CRM и напоминания; парсер по пяти площадкам; подбор артистов под звучание бита.

---
---

# MessedUpSearch (English)

Desktop CRM and beat library for music producers. Keep your beats organized, maintain a roster of underground artists, link beats to the people you pitch them to, and get reminded when a pitch is still hanging.

## What it does

A local-first tool that turns a folder of beats and a list of artists into a working pitch pipeline.

### Beat library
- Add a single beat through the system file picker (WAV / MP3) with manual metadata: name, BPM, key, genre tags.
- Bulk-import a whole folder at once — every WAV/MP3 is added in one action, and re-importing doesn't create duplicates (deduplicated by file path).
- Beat status: potential, free, or sold (sold ones carry a license type). Status is shown as a colored marker in the table.
- Edit and delete by clicking a row.
- Built-in player: ▶ on each beat row, a bar above the status line with a pixel waveform (click to seek), ⏮ ⏭ and auto-advance through the filtered list, remembered volume. Space pauses, ←/→ seek ±5 seconds.
- An artist card lists their tracks from the platforms, playable right there without opening SoundCloud.

### Artists
- Manual entry: nickname, links (SoundCloud / Instagram / Spotify), language, genre tags, plays.
- Avatars downloaded by the parser show up in the table and on the artist card; artists without a photo keep a colored circle.
- Language is detected from track titles and the profile bio: Cyrillic, Vietnamese diacritics, kana, hangul, Han characters and Arabic are recognised by script, European languages by stop words.
- Favorite and red-flag toggles, switched with one click right in the table.
- CRM status (no reply / ok / posted free) and free-form notes.
- Deduplication by profile link: artists without a link can be added freely, while a given link stays unique.

### Beat-to-artist links
- Assign one beat to several artists via checkboxes on the beat card.
- The artist card shows the beats lined up for them.
- Each link stores the assignment date and a sent flag — the foundation for CRM and reminders.

### Filtering & search
- Both tables filter live: by genre, key (beats) / language (artists), BPM or plays range, period, and free text.
- Filters combine and apply instantly.

### CRM
- A dedicated window for the artists you're working with (those with a CRM status set).
- Per artist: status, notes, and the list of assigned beats marked pending or sent.
- A "mark as sent" button right on the card.

### Bilingual interface
- Russian and English switch in Settings and apply immediately, no restart. The choice persists between launches.

### Reminders
- Assigned a beat but never sent it? After a configurable number of days the app shows a reminder on startup, grouped per artist.
- The threshold (1–14 days) is a slider in Settings and persists between launches.
- Mark a beat sent and the reminder disappears.


### Artist parser
- Searches five platforms at once: Audius, SoundCloud, Bandcamp, Last.fm and Jamendo. Each can be switched off with a checkbox.
- Criteria: genre or tag, plays range, release freshness, country, and how many artists to take per platform.
- Collects nickname, profile link, avatar, plays, last release date, tags and tracks; socials are enriched through Genius.
- Results arrive as a checklist — only what you tick lands in the database. Re-runs don't create duplicates and never touch notes, CRM status or flags.
- One platform failing doesn't kill the run: the rest finish, and the report shows who dropped out and how much they managed to collect.
- Last.fm, Jamendo and Genius need free API keys, entered in Settings.

### Similar artists (by sound)
- The ANALYZE button on a beat card shows who it resembles **by sound**, not by tags: `Osamason 87% · Summrs 81% · Autumn! 74%`.
- Powered by Discogs-EffNet (Essentia, MTG / Universitat Pompeu Fabra), a network trained to tell apart 400 Discogs music styles. It turns a beat into a 1280-number vector; on top sits our own "head", fine-tuned on 652 underground artists so that tracks by one artist land close together.
- Results come in two parts: "sounds like" (30 big artists as a style reference) and up to 10 reachable underground artists from a crawled index, each with ▶ play their track, ➕ add to your artists, ↗ open profile.
- Beats are matched against artists' INSTRUMENTALS (vocals are removed when the index is built).
- Honest number on the actual task (type beats, the artist never seen in training): the right artist is in the top five of 30 references 53% of the time (random 17%); among all 682 artists — in the top ten 13.5% of the time. The model was picked by measurement out of five (MERT, MuQ, CLAP, EffNet variants) — see `ml/README.md`.
- The model (18 MB) ships with the app: analysis works right away, about a second per beat. Only the artist index (~6 MB) is downloaded.
- Results are stored in the database and not recomputed every time you open the card.
- The reverse view: an artist card shows which of your beats fit them.
- Model license: CC BY-NC-SA 4.0 — non-commercial use only.

Apart from downloading the artist index, all data lives in a local SQLite file on your machine — nothing is uploaded anywhere. A fresh install starts empty, with a dimmed preview of how a filled table looks.

## Tech stack

| Layer | Tech |
| :--- | :--- |
| UI | Avalonia 12 (XAML), FluentTheme, MVVM |
| Language | C# / .NET 10 |
| MVVM | CommunityToolkit.Mvvm (source-generated properties & commands) |
| Data | SQLite + Entity Framework Core 10 (code-first migrations) |
| Audio | SoundFlow (miniaudio) — playback |
| Settings | local `settings.json` |

## Architecture

```
App.axaml.cs                 apply EF migrations -> open MainWindow
   |
MainWindow (MainWindowViewModel)
   |-- tabs: Beats / Artists  (switch CurrentViewModel)
   |-- overlays: CRM / Settings / Reminders / Parser
   |
   +-- BeatsView   <- BeatsViewModel    (CRUD beats, filters, assign artists, similarity)
   +-- ArtistsView <- ArtistsViewModel  (CRUD artists, filters, flags, clickable links)
   |
Services/Parsing              five platforms behind one IArtistSource interface
   +-- ParserService          drives sources, collects progress and failures
   +-- Sources/               Audius, SoundCloud, Bandcamp, LastFm, Jamendo
   +-- GeniusLookup           socials and language on top of what was found
   +-- ArtistImportService    upsert into the database, avatars, tracks
   |
Services/Ml                   beat-to-artist similarity
   +-- AudioDecoder           WAV/MP3 -> mono 16 kHz (own windowed-sinc resampler, central minute)
   +-- EffNetEmbedder         audio -> Essentia mel -> EffNet (ONNX) -> head; one session per app
   +-- BeatSimilarityService  beat vector -> "sounds like" + top 10 reachable artists
   +-- TargetIndex/IndexStore v3 indexes: references (bundled) and reachable artists (downloaded)
   +-- HubCorrection          correction for artists that resemble everything
   +-- MlAssets               where the model, head and indexes live
   |
Data layer (EF Core)
   +-- AppDbContext --> SQLite app.db
        |-- Artists
        |-- ArtistTracks       (tracks found by the parser)
        |-- TrackEmbeddings    (a track's sound vector, 3 KB; audio is not stored)
        |-- Beats
        |-- BeatSimilarities   (which artists a beat resembles: rank, percent)
        +-- SentBeatsLog       (beat <-> artist links: assigned / sent / date)

ml/                          Python workshop: dataset, embeddings, metrics, ONNX export
                             (not part of the running app)
```

Database location: `%AppData%/MessedUpSearch/app.db` (Windows), `~/Library/Application Support/MessedUpSearch/app.db` (macOS).

## Build & run

```bash
dotnet build
dotnet run --project MessedUpSearchA/MessedUpSearchA.csproj

# UI dev loop (XAML + method-body hot reload)
dotnet watch --project MessedUpSearchA/MessedUpSearchA.csproj

# release build
dotnet publish MessedUpSearchA/MessedUpSearchA.csproj -c Release
```

Cross-platform: Windows and macOS.

## Roadmap (v2)

Intentionally out of scope for the current version and planned next:

- **Genre labels on top of similarity** — group artists into clusters (rage, plugg, jerk) and tag a beat directly instead of only listing lookalikes.
- **Automatic BPM and key detection** — currently typed by hand.
- **Lyrics-based language detection** — language is currently guessed from the profile's country and the Genius bio, which works poorly.
- **Email & Telegram reminders** — deliver pitch reminders outside the app, not just on startup.

## Status

v0.5 — beats, artists, links, filters, CRM and reminders; a five-platform parser; sound-based artist matching.

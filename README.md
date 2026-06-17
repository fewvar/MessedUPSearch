# MessedUpSearch

Десктопная CRM и библиотека битов для музыкальных продюсеров. Держи свои биты в порядке, веди базу андеграунд-артистов, привязывай биты к тем, кому их питчишь, и получай напоминания, когда питч завис.

<!-- TODO: добавить скриншот и/или GIF-демо -->
<!-- ![MessedUpSearch — beats](docs/screenshot-beats.png) -->
<!-- ![MessedUpSearch — artists](docs/screenshot-artists.png) -->

---

## Что умеет

Локальный инструмент, который превращает папку битов и список артистов в рабочий питч-пайплайн.

### Библиотека битов
- Добавление бита по одному через системный файловый пикер (WAV / MP3) с ручным вводом метаданных: название, BPM, тональность, жанровые теги.
- Массовый импорт целой папки разом — все WAV/MP3 заливаются одним действием, повторный импорт не плодит дубликаты (дедупликация по пути файла).
- Статус бита: «потенциал», «фри» или «продан» (для проданного указывается тип лицензии). Статус подсвечивается цветным маркером в таблице.
- Редактирование и удаление по клику на строку.

### Артисты
- Ручное добавление: никнейм, ссылки (SoundCloud / Instagram / Spotify), язык, жанровые теги, прослушивания.
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

### Напоминания
- Привязал бит, но так и не отправил? Через настраиваемое число дней приложение при запуске покажет напоминание, сгруппированное по артисту.
- Порог (1–14 дней) задаётся ползунком в настройках и сохраняется между запусками.
- Отметил бит отправленным — напоминание исчезает.

Все данные лежат в локальном файле SQLite на твоём компьютере — ничего никуда не загружается. Свежая установка стартует пустой, с приглушённым превью того, как выглядит заполненная таблица.

## Технологии

| Слой | Технология |
| :--- | :--- |
| UI | Avalonia 12 (XAML), FluentTheme, MVVM |
| Язык | C# / .NET 9 |
| MVVM | CommunityToolkit.Mvvm (генерация свойств и команд) |
| Данные | SQLite + Entity Framework Core 9 (code-first миграции) |
| Настройки | локальный `settings.json` |

## Архитектура

```
App.axaml.cs                 применить миграции EF -> открыть MainWindow
   |
MainWindow (MainWindowViewModel)
   |-- вкладки: Beats / Artists  (переключение CurrentViewModel)
   |-- оверлеи: CRM / Settings / Reminders
   |
   +-- BeatsView   <- BeatsViewModel    (CRUD битов, фильтры, привязка артистов)
   +-- ArtistsView <- ArtistsViewModel  (CRUD артистов, фильтры, флаги)
   |
Слой данных (EF Core)
   +-- AppDbContext --> SQLite app.db
        |-- Artists
        |-- Beats
        +-- SentBeatsLog   (связи бит <-> артист: назначен / отправлен / дата)
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

- **Парсер SoundCloud** — автосбор андеграунд-артистов (ник, аватар, прослушивания, соцсети, последний релиз) через приватный API.
- **Аудио-классификатор жанра** — анализ звучания бита через `librosa` (Python, упаковка PyInstaller) и автотегирование жанра/BPM/тональности вместо ручного ввода.
- **Определение языка** — Genius API с фолбэком на speech-to-text для языка лирики артиста.
- **Email и Telegram-напоминания** — доставка напоминаний вне приложения, а не только при запуске.

## Статус

v0.3 — полностью рабочая локальная версия: биты, артисты, связи, фильтры, CRM и напоминания.

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

### Artists
- Manual entry: nickname, links (SoundCloud / Instagram / Spotify), language, genre tags, plays.
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

### Reminders
- Assigned a beat but never sent it? After a configurable number of days the app shows a reminder on startup, grouped per artist.
- The threshold (1–14 days) is a slider in Settings and persists between launches.
- Mark a beat sent and the reminder disappears.

All data lives in a local SQLite file on your machine — nothing is uploaded anywhere. A fresh install starts empty, with a dimmed preview of how a filled table looks.

## Tech stack

| Layer | Tech |
| :--- | :--- |
| UI | Avalonia 12 (XAML), FluentTheme, MVVM |
| Language | C# / .NET 9 |
| MVVM | CommunityToolkit.Mvvm (source-generated properties & commands) |
| Data | SQLite + Entity Framework Core 9 (code-first migrations) |
| Settings | local `settings.json` |

## Architecture

```
App.axaml.cs                 apply EF migrations -> open MainWindow
   |
MainWindow (MainWindowViewModel)
   |-- tabs: Beats / Artists  (switch CurrentViewModel)
   |-- overlays: CRM / Settings / Reminders
   |
   +-- BeatsView   <- BeatsViewModel    (CRUD beats, filters, assign artists)
   +-- ArtistsView <- ArtistsViewModel  (CRUD artists, filters, flags)
   |
Data layer (EF Core)
   +-- AppDbContext --> SQLite app.db
        |-- Artists
        |-- Beats
        +-- SentBeatsLog   (beat <-> artist links: assigned / sent / date)
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

- **SoundCloud parser** — auto-collect underground artists (nickname, avatar, plays, socials, last release) via the private API.
- **Audio genre classifier** — analyze each beat's sound with `librosa` (Python, packed with PyInstaller) and auto-tag genre/BPM/key instead of typing them by hand.
- **Language detection** — Genius API with a speech-to-text fallback for an artist's lyrics language.
- **Email & Telegram reminders** — deliver pitch reminders outside the app, not just on startup.

## Status

v0.3 — fully working local version: beats, artists, links, filters, CRM and reminders.

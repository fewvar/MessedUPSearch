# MessedUpSearch

Desktop CRM and beat library for music producers. Keep your beats organized, tag underground artists, link beats to the people you pitch them to, and get reminded when a pitch is still hanging.

> **Used in the real world:** an active producer uses this tool to sort their own beats by artist and track who they've sent what to.

<!-- TODO: добавить скриншот и/или GIF-демо -->
<!-- ![MessedUpSearch — beats](docs/screenshot-beats.png) -->
<!-- ![MessedUpSearch — artists](docs/screenshot-artists.png) -->

---

## What it does

A local-first tool that turns a folder of beats and a list of artists into a working pitch pipeline:

- **Beats library** — add beats one by one (file picker) or import a whole folder at once. Each beat carries name, BPM, key, genre tags and a status: 🟡 potential / ⚫ free / 🟢 sold (with license type).
- **Artists** — add artists manually with nickname, links (SoundCloud / Instagram / Spotify), language and genre. Mark favorites ★ and red-flags 🚩 with one click.
- **Pitch links** — assign a beat to one or more artists. Each artist card shows the beats lined up for them.
- **Filtering & search** — filter both tables live by genre, key/language, BPM/plays range, period, and free text.
- **CRM** — a dedicated window for artists you're working with: their status, notes, and the beats you've assigned, each marked ⏳ pending or ✓ sent.
- **Reminders** — assign a beat but never send it? After a configurable number of days the app greets you on startup with a reminder, grouped per artist. Mark it sent and it's gone. Threshold is a slider in Settings.

All data lives in a local SQLite file on your machine — nothing is uploaded anywhere. A fresh install starts empty (with a greyed-out preview of how a filled table looks).

---

## Tech stack

| Layer | Tech |
| :--- | :--- |
| UI | Avalonia 12 (XAML), FluentTheme, MVVM |
| Language | C# / .NET 9 |
| MVVM | CommunityToolkit.Mvvm (source-generated properties & commands) |
| Data | SQLite + Entity Framework Core 9 (code-first migrations) |
| Settings | local `settings.json` |

---

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

---

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

---

## Roadmap (v2)

These are intentionally out of scope for the current version and planned next:

- **SoundCloud parser** — auto-collect underground artists (nickname, avatar, plays, socials, last release) via the private API.
- **Audio genre classifier** — analyze each beat's sound with `librosa` (Python, packed with PyInstaller) and auto-tag genre/BPM/key instead of typing them by hand.
- **Language detection** — Genius API → speech-to-text fallback for an artist's lyrics language.
- **Email & Telegram reminders** — deliver pitch reminders outside the app, not just on startup.

---

## Status

v0.3 — fully working local version: beats, artists, links, filters, CRM and reminders. Built as an engineering portfolio piece and actually used by a working producer.

<p align="center">
  <img src="docs/banner.png" alt="MessedUpSearch" width="100%">
</p>

<p align="center">
  <a href="README.md">Русский</a> · <b>English</b>
</p>

<p align="center">
  <a href="https://github.com/fewvar/MessedUPSearch/releases/latest"><img src="https://img.shields.io/github/v/release/fewvar/MessedUPSearch?style=flat-square&color=1a1a1a" alt="release"></a>
  <img src="https://img.shields.io/badge/macOS-Apple%20Silicon-1a1a1a?style=flat-square&logo=apple" alt="macOS">
  <img src="https://img.shields.io/badge/Windows-x64-1a1a1a?style=flat-square&logo=windows" alt="Windows">
  <img src="https://img.shields.io/github/downloads/fewvar/MessedUPSearch/total?style=flat-square&color=1a1a1a" alt="downloads">
</p>

An app for beatmakers: all your beats in one table, a roster of underground artists, a record of who got what — and artists to pitch a beat to, picked **by how the beat sounds**.

<p align="center">
  <a href="https://github.com/fewvar/MessedUPSearch/releases/latest/download/MessedUpSearch.app.zip"><b>Download for macOS</b></a>
  &nbsp;·&nbsp;
  <a href="https://github.com/fewvar/MessedUPSearch/releases/latest/download/MessedUpSearch-win-x64.exe"><b>Download for Windows</b></a>
</p>

---

## Who to pitch a beat to

Open a beat, hit **ANALYZE** — a second later the app shows:

- **"sounds like"** — three big artists whose style the beat resembles;
- **who to pitch** — ten underground artists (1,000 to 300,000 plays) from a base of 650+ rappers on SoundCloud and Audius. Each one has ▶ play the track of theirs that is **closest to your beat** — you hear right away why they made the list; ＋ add to your roster; ↗ open the profile to reach out.

<p align="center"><img src="docs/en-analyze.jpg" alt="Beat analysis" width="90%"></p>

It compares the actual sound: a neural network listens to the beat and compares it with the instrumentals of the artists' tracks (vocals are removed beforehand). No genre tags or BPM needed.

Treat it as a shortlist to listen through, not a ready mailing list: play the candidates with ▶ and pick the ones that really fit. In testing, the right artist lands in the top five of 30 big names about half the time — three times better than chance, but not magic.

Everything the analysis finds is saved: an artist's card shows which of your beats fit them.

## Beats and player

- Add beats one by one (WAV / MP3) or import a whole folder — re-importing never creates duplicates.
- Name, genre, BPM, key and status: **potential**, **free** or **sold** (with license type).
- Filter by genre, BPM, key, date and search by name — all instant. Click a column header to sort: ▼ → ▲ → back.
- Built-in player with a waveform: ▶ in the beat row, click the waveform to seek, previous / next beat, volume is remembered. Space pauses, ← / → seek.

<p align="center"><img src="docs/en-player.jpg" alt="Beat library and player" width="90%"></p>

## Artists and finding new ones

- Artist roster: avatar, nickname, genre, plays, SoundCloud / Instagram / Spotify links, language, notes.
- ★ favorite and 🚩 red flag — one click right in the table.
- The **parser** searches five platforms at once — Audius, SoundCloud, Bandcamp, Last.fm, Jamendo — by genre, play count, release freshness and country. Socials are pulled from Genius. Only the artists you tick get added.

<p align="center">
  <img src="docs/en-artists.jpg" alt="Artists" width="49%">
  <img src="docs/en-parser.jpg" alt="Parser" width="49%">
</p>

## Who got what

- Link a beat to one or more artists right from the beat card.
- The **CRM** shows everyone you're working with: status (no reply / ok / posted free), notes, which beats were sent and which are still waiting.
- Linked a beat but never sent it? After a set number of days the app reminds you on startup.

<p align="center"><img src="docs/en-crm.jpg" alt="CRM" width="90%"></p>

## Small things

- Russian and English interface, switches instantly, no restart.
- Smooth animations following Apple's motion principles and a few quiet sounds (analysis done, beat sent, ★, parser finished) — both can be turned off in Settings; the system Reduce Motion setting is respected.
- All data stays on your computer. The app only goes online to search for artists, stream their tracks in the player and, once, to fetch the underground artist base.

<p align="center"><img src="docs/en-settings.jpg" alt="Settings" width="70%"></p>

## Install

**macOS** (Apple Silicon): download [`MessedUpSearch.app.zip`](https://github.com/fewvar/MessedUPSearch/releases/latest/download/MessedUpSearch.app.zip), unzip and drag it to Applications. On first launch: right-click → Open (the app isn't signed with an Apple certificate).

**Windows**: download [`MessedUpSearch-win-x64.exe`](https://github.com/fewvar/MessedUPSearch/releases/latest/download/MessedUpSearch-win-x64.exe) and run it. If SmartScreen warns about an unknown publisher — "More info" → "Run anyway".

The parser needs free Last.fm, Jamendo and Genius keys — the links to get them are right in Settings.

---

<sub>Sound matching uses the Discogs-EffNet model (Essentia, MTG / Universitat Pompeu Fabra), licensed CC BY-NC-SA 4.0 — non-commercial use only. Made by <a href="https://github.com/fewvar">@fewvar</a>.</sub>

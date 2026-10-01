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

An app for beatmakers: all your beats in one table, a roster of underground artists, artists picked **by how the beat sounds**, pitching from your own email and a CRM that tracks replies by itself.

<p align="center">
  <a href="https://github.com/fewvar/MessedUPSearch/releases/latest/download/MessedUpSearch.app.zip"><b>Download for macOS</b></a>
  &nbsp;·&nbsp;
  <a href="https://github.com/fewvar/MessedUPSearch/releases/latest/download/MessedUpSearch-win-x64.exe"><b>Download for Windows</b></a>
</p>

---

## Who to pitch a beat to

Open a beat, hit **ANALYZE** — a second later the app shows:

- **"sounds like"** — three big artists whose style the beat resembles;
- **who to pitch** — ten underground artists (1,000 to 300,000 plays) from a base of 650+ rappers on SoundCloud and Audius. Each one has ▶ play the track of theirs that is **closest to your beat** — you hear right away why they made the list; ＋ add to your roster; ↗ open the profile. Tick the ones that fit and pitch the beat to all of them at once.

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

## Pitching from your own email

- The artist card has email, Telegram and another contact. "Find in profile" pulls contacts from the SoundCloud and Audius description (even "name (at) gmail (dot) com") — they go into the card only when you click.
- **Pitch** a beat right from its card or from the analysis results with checkboxes. Emails go out from your own mailbox (Gmail, Yandex, Mail.ru, iCloud or any other), with a link to the beat, one every 20–40 seconds and no more than a daily limit — so your mailbox isn't flagged as spam. Pitching runs in the background, you can close the window.
- Email templates with placeholders (`{artist}`, `{beat}`, `{bpm}`, `{key}`, `{link}`, `{my_name}`) and a preview for every artist before sending.
- Spam protection: artists who already got this beat, who you wrote to less than a week ago, or who ignored three emails in a row are unticked, with the reason shown.
- **Bring to life** — AI with your own key (Groq, OpenRouter or any OpenAI-compatible server) rewrites the email for each artist: their nickname, genre, one track. It is told not to invent facts and the beat link is checked — anything that fails goes out as the template.
- The email password and the AI key live in the system keychain (Keychain / Windows Credential Manager), not in the settings file.

<p align="center"><img src="docs/en-mail.jpg" alt="Pitching" width="90%"></p>

## A CRM that keeps itself

- Every email is logged automatically: which beat, to whom, when, which template.
- Every few minutes the app checks your inbox for artists' replies — the CRM shows "replied" and the first line. The rest of your mail is not read.
- No reply for a week (configurable) — **Follow-ups** in one click: the reminder goes into the same thread, one per beat.
- Linked a beat by hand but never sent it — the app reminds you.
- Email opens are not tracked: that needs a server of its own, and emails go straight from your mailbox.

<p align="center"><img src="docs/en-crm.jpg" alt="CRM" width="90%"></p>

## Statistics and "Today"

- **Statistics** for a week, a month, three months or all time: reply rate by template, beat genre, day and hour sent, how fast artists reply, which beats get replies, which artists reply (genre, plays).
- **Today** — tips for the day, strictly about beats and pitching: when you usually make beats, which genre and tempo get more replies, when to send, what's sitting unsent and who needs a follow-up. With an AI key — a two-sentence piece of advice; only these numbers are sent to it, no emails or names.
- Optional DAW session tracking (FL Studio, Ableton, Logic and others): once a minute the app checks whether a DAW is in front and keeps only intervals like "FL Studio 14:05–16:40". No window titles or project names, only on your computer, erased with one button. It's on only with your consent — together with a tray icon and launch with the system, so replies and pitching keep working with the window closed.

<p align="center">
  <img src="docs/en-stats.jpg" alt="Statistics" width="49%">
  <img src="docs/en-today.jpg" alt="Today" width="49%">
</p>

## Small things

- Russian and English interface, switches instantly, no restart.
- Smooth animations following Apple's motion principles and a few quiet sounds (analysis done, beat sent, ★, parser finished) — both can be turned off in Settings; the system Reduce Motion setting is respected.
- All data stays on your computer. The app only goes online to search for artists, stream their tracks in the player, once to fetch the underground artist base — and to your mailbox and the AI, if you connect them.
- "Report a problem" in Settings drafts an email with the version and the log tail — you send it yourself from your mail app.

<p align="center"><img src="docs/en-settings.jpg" alt="Settings" width="70%"></p>

## Install

**macOS** (Apple Silicon): download [`MessedUpSearch.app.zip`](https://github.com/fewvar/MessedUPSearch/releases/latest/download/MessedUpSearch.app.zip), unzip and drag it to Applications. On first launch: right-click → Open (the app isn't signed with an Apple certificate).

**Windows**: download [`MessedUpSearch-win-x64.exe`](https://github.com/fewvar/MessedUPSearch/releases/latest/download/MessedUpSearch-win-x64.exe) and run it. If SmartScreen warns about an unknown publisher — "More info" → "Run anyway".

The parser needs free Last.fm, Jamendo and Genius keys — the links to get them are right in Settings.

---

<sub>Sound matching uses the Discogs-EffNet model (Essentia, MTG / Universitat Pompeu Fabra), licensed CC BY-NC-SA 4.0 — non-commercial use only. Made by <a href="https://github.com/fewvar">@fewvar</a>.</sub>

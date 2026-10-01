using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.Services.Parsing;

public class ImportOutcome
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int TracksSaved { get; set; }
    public int AvatarsSaved { get; set; }
    public List<string> Problems { get; } = new();

    public string Summary
    {
        get
        {
            var parts = new List<string>();

            if (Added > 0) parts.Add($"добавлено {Added}");
            if (Updated > 0) parts.Add($"обновлено {Updated}");
            if (TracksSaved > 0) parts.Add($"треков {TracksSaved}");
            if (parts.Count == 0) parts.Add("ничего не изменилось");

            var line = string.Join(", ", parts);
            return Problems.Count > 0 ? $"{line}. Проблем: {Problems.Count}" : line;
        }
    }
}

public class ArtistImportService
{
    private readonly string _mediaFolder;
    private readonly Func<string, CancellationToken, Task<byte[]>> _download;

    public ArtistImportService(
        string mediaFolder,
        Func<string, CancellationToken, Task<byte[]>>? download = null)
    {
        _mediaFolder = mediaFolder;
        _download = download ?? ParsingHttp.GetBytesAsync;
    }

    public async Task<ImportOutcome> ImportAsync(
        IReadOnlyList<ArtistCandidate> candidates, CancellationToken ct = default)
    {
        var outcome = new ImportOutcome();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(candidate.SourceUrl) ||
                string.IsNullOrWhiteSpace(candidate.Nickname))
            {
                outcome.Problems.Add(
                    $"пропущен кандидат без {(string.IsNullOrWhiteSpace(candidate.Nickname) ? "ника" : "ссылки")}");
                continue;
            }

            try
            {
                var avatarPath = await SaveAvatarAsync(candidate, ct);
                if (!string.IsNullOrEmpty(avatarPath))
                    outcome.AvatarsSaved++;

                using var db = new AppDbContext();

                var artist = FindExisting(db, candidate);
                var isNew = artist is null;

                if (artist is null)
                {
                    artist = new Artist
                    {
                        Nickname = candidate.Nickname,
                        CreatedAt = now,
                        CrmStatus = string.Empty,
                        Notes = string.Empty,
                        // Запасной кружок на случай, если аватарка не скачалась
                        // или файл потом пропадёт. Без этого все были серыми.
                        AvatarColor = PickColor(candidate.Nickname)
                    };
                    db.Artists.Add(artist);
                }

                ApplyParsedFields(artist, candidate, avatarPath, now);

                db.SaveChanges();

                if (isNew) outcome.Added++;
                else outcome.Updated++;

                outcome.TracksSaved += SaveTracks(db, artist.Id, candidate, now);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                outcome.Problems.Add($"{candidate.Nickname}: {ex.Message}");
            }
        }

        return outcome;
    }

    private static Artist? FindExisting(AppDbContext db, ArtistCandidate candidate)
    {
        var url = candidate.SourceUrl;

        if (string.IsNullOrWhiteSpace(url))
            return null;

        return db.Artists.FirstOrDefault(a => a.SourceUrl == url)
            ?? db.Artists.FirstOrDefault(a => a.ScLink == url);
    }

    private static void ApplyParsedFields(Artist artist, ArtistCandidate candidate, string avatarPath, string now)
    {

        artist.SourcePlatform = candidate.Platform;
        artist.SourceUrl = candidate.SourceUrl;
        artist.LastParsed = now;

        if (candidate.TotalPlays > 0)
            artist.TotalPlays = candidate.TotalPlays;

        if (!string.IsNullOrWhiteSpace(candidate.LastTrackDate))
            artist.LastTrackDate = candidate.LastTrackDate;

        if (!string.IsNullOrWhiteSpace(candidate.Tags))
            artist.AiGenreTags = candidate.Tags;

        if (!string.IsNullOrWhiteSpace(candidate.Description))
            artist.Bio = candidate.Description;

        if (candidate.Platform == "SoundCloud" && string.IsNullOrWhiteSpace(artist.ScLink))
            artist.ScLink = candidate.SourceUrl;

        FillIfEmpty(() => artist.Nickname, v => artist.Nickname = v, candidate.Nickname);
        FillIfEmpty(() => artist.IgLink, v => artist.IgLink = v, candidate.IgLink);
        FillIfEmpty(() => artist.SpotifyLink, v => artist.SpotifyLink = v, candidate.SpotifyLink);
        FillIfEmpty(() => artist.LinktreeLink, v => artist.LinktreeLink = v, candidate.Website);
        FillIfEmpty(() => artist.AvatarPath, v => artist.AvatarPath = v, avatarPath);

        var language = string.IsNullOrWhiteSpace(candidate.Language)
            ? GuessLanguage(candidate.Country)
            : candidate.Language;
        FillIfEmpty(() => artist.Language, v => artist.Language = v, language);

        // Notes, CrmStatus, IsFavorite, IsRedFlagged — ручные поля, парсер их не трогает.
    }

    private static void FillIfEmpty(Func<string> get, Action<string> set, string value)
    {
        if (string.IsNullOrWhiteSpace(get()) && !string.IsNullOrWhiteSpace(value))
            set(value);
    }

    private int SaveTracks(AppDbContext db, int artistId, ArtistCandidate candidate, string now)
    {
        var saved = 0;

        foreach (var track in candidate.Tracks)
        {
            if (string.IsNullOrWhiteSpace(track.Url))
                continue;

            var existing = db.ArtistTracks.FirstOrDefault(t => t.Url == track.Url);

            if (existing is null)
            {
                db.ArtistTracks.Add(new ArtistTrack
                {
                    ArtistId = artistId,
                    Title = track.Title,
                    Url = track.Url,
                    SourcePlatform = candidate.Platform,
                    SourceTrackId = track.SourceId,
                    PlayCount = track.PlayCount,
                    ReleasedAt = track.ReleasedAt,
                    Tags = track.Tags,
                    IsDownloadable = track.IsDownloadable,
                    FetchedAt = now
                });
                saved++;
            }
            else
            {
                existing.PlayCount = track.PlayCount;
                existing.IsDownloadable = track.IsDownloadable;
                existing.FetchedAt = now;

                // Треки, сохранённые до v0.7, пришли без id — добиваем при повторном парсинге.
                if (string.IsNullOrWhiteSpace(existing.SourceTrackId))
                    existing.SourceTrackId = track.SourceId;
            }
        }

        db.SaveChanges();
        return saved;
    }

    private async Task<string> SaveAvatarAsync(ArtistCandidate candidate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(candidate.AvatarUrl))
            return string.Empty;

        var folder = Path.Combine(_mediaFolder, "avatars");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, $"{Sanitize(candidate.Platform)}_{Sanitize(candidate.Nickname)}.jpg");

        if (File.Exists(path))
            return path;

        var bytes = await _download(candidate.AvatarUrl, ct);
        await File.WriteAllBytesAsync(path, bytes, ct);

        return path;
    }

    /// <summary>
    /// Цвет по нику, а не случайный: один и тот же артист всегда получает свой,
    /// и при повторном импорте кружок не перекрашивается.
    /// </summary>
    private static string PickColor(string nickname)
    {
        string[] palette = { "#E74C3C", "#5DADE2", "#2ECC71", "#F1C40F", "#9B59B6", "#E67E22", "#1ABC9C" };

        var hash = 0;
        foreach (var c in nickname)
            hash = (hash * 31 + c) & 0x7FFFFFFF;

        return palette[hash % palette.Length];
    }

    private static string Sanitize(string raw)
    {
        var cleaned = new string(raw
            .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_')
            .ToArray());

        cleaned = cleaned.Trim('_');

        if (cleaned.Length == 0)
            cleaned = "unnamed";

        return cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }

    private static string GuessLanguage(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return "Common";

        var map = new (string Needle, string Language)[]
        {
            ("russia", "Russian"), ("россия", "Russian"), ("moscow", "Russian"),
            ("ukraine", "Russian"), ("belarus", "Russian"), ("kazakhstan", "Russian"),
            ("france", "French"), ("paris", "French"),
            ("germany", "German"), ("berlin", "German"),
            ("spain", "Spanish"), ("mexico", "Spanish"), ("argentina", "Spanish"),
            ("chile", "Spanish"), ("colombia", "Spanish"), ("madrid", "Spanish"),
            ("usa", "English"), ("united states", "English"), ("uk", "English"),
            ("england", "English"), ("london", "English"), ("canada", "English"),
            ("australia", "English"), ("new york", "English"), ("los angeles", "English"),
            ("vietnam", "Vietnamese"), ("viet nam", "Vietnamese"), ("hanoi", "Vietnamese"),
            ("saigon", "Vietnamese"), ("ho chi minh", "Vietnamese"),
            ("brazil", "Portuguese"), ("portugal", "Portuguese"), ("lisbon", "Portuguese"),
            ("italy", "Italian"), ("rome", "Italian"), ("milan", "Italian"),
            ("japan", "Japanese"), ("tokyo", "Japanese"),
            ("korea", "Korean"), ("seoul", "Korean"),
            ("china", "Chinese"), ("beijing", "Chinese"), ("shanghai", "Chinese")
        };

        foreach (var (needle, language) in map)
        {
            if (location.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return language;
        }

        return "Common";
    }
}

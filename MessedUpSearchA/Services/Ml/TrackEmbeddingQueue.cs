using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;
using MessedUpSearchA.Services.Parsing;
using MessedUpSearchA.Services.Parsing.Sources;

namespace MessedUpSearchA.Services.Ml;

public enum EmbeddingQueueState
{
    Idle,
    DownloadingModel,
    Listening,
    Paused,
    Failed
}

/// <summary>
/// Фоновая очередь «послушать треки артистов»: скачать звук, прогнать через MERT,
/// сохранить вектор. Звук на диске не остаётся — только вектор.
///
/// Работает по одному треку: сеть и модель всё равно узкое место, а параллельные
/// закачки с SoundCloud быстрее приводят к бану, чем к результату.
///
/// Какие треки берём: у каждого артиста до <see cref="TracksPerArtist"/> самых
/// прослушиваемых. Ранжирование и так смотрит на три ближайших трека, так что
/// больше шести почти ничего не даёт, а трафик растёт линейно.
/// Если у артиста звука нет вовсе (Last.fm, Bandcamp, Jamendo, или все треки
/// оказались сниппетами) — один раз ищем его на Deezer и берём превью.
/// </summary>
public sealed class TrackEmbeddingQueue
{
    public const int TracksPerArtist = 6;
    public const int MaxAttempts = 3;

    /// <summary>Короче этого — не трек, а интро или сниппет; вектор по нему случайный.</summary>
    private const int MinSeconds = 20;

    private const int DeezerTracksPerArtist = 10;

    private readonly object _gate = new();
    private readonly Dictionary<string, IAudioResolver> _resolvers;
    private readonly DeezerSource _deezer;

    private CancellationTokenSource? _cts;
    private bool _running;

    private TrackEmbeddingQueue()
    {
        _deezer = new DeezerSource();

        _resolvers = new IAudioResolver[] { new SoundCloudSource(), new AudiusSource(), _deezer }
            .ToDictionary(r => r.Platform, StringComparer.OrdinalIgnoreCase);
    }

    public static TrackEmbeddingQueue Instance { get; } = new();

    public EmbeddingQueueState State { get; private set; } = EmbeddingQueueState.Idle;

    /// <summary>Сколько треков сделано в текущем проходе и сколько было в нём всего.</summary>
    public int Done { get; private set; }
    public int Total { get; private set; }

    public double ModelProgress { get; private set; }

    public string LastError { get; private set; } = string.Empty;

    /// <summary>Вызывается из фонового потока — подписчик сам переносит его в UI.</summary>
    public event Action? StateChanged;

    private static string TempDirectory => Path.Combine(AppPaths.DataDir, "tmp-audio");

    /// <summary>Проверить, есть ли работа, и если есть — начать. Повторный вызов на ходу ничего не делает.</summary>
    public void Kick()
    {
        lock (_gate)
        {
            if (_running || State == EmbeddingQueueState.Paused)
                return;

            _running = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            Task.Run(() => RunAsync(ct));
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            SetState(EmbeddingQueueState.Paused);
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (State != EmbeddingQueueState.Paused)
                return;

            SetState(EmbeddingQueueState.Idle);
        }

        Kick();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        CleanTempDirectory();

        // Deezer не ответил — пометку не ставим, но и в этом проходе не повторяем,
        // иначе без сети цикл крутился бы вечно.
        var triedDeezer = new HashSet<int>();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var plan = BuildPlan();
                plan.DeezerLookups.RemoveAll(a => triedDeezer.Contains(a.ArtistId));

                if (plan.Tracks.Count == 0 && plan.DeezerLookups.Count == 0)
                    break;

                if (plan.Tracks.Count > 0 && !await EnsureModelAsync(ct))
                    return;

                Done = 0;
                Total = plan.Tracks.Count;
                SetState(EmbeddingQueueState.Listening);

                foreach (var job in plan.Tracks)
                {
                    ct.ThrowIfCancellationRequested();
                    await ProcessTrackAsync(job, ct);
                    Done++;
                    StateChanged?.Invoke();
                }

                foreach (var artist in plan.DeezerLookups)
                {
                    ct.ThrowIfCancellationRequested();
                    triedDeezer.Add(artist.ArtistId);
                    await LookupDeezerAsync(artist, ct);
                }
            }

            if (!ct.IsCancellationRequested)
                SetState(EmbeddingQueueState.Idle);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Пауза. Состояние уже выставил Pause().
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            // До сервера не достучались вообще — это сеть, а не трек. Останавливаемся,
            // иначе за минуту без интернета все треки сожгли бы свои попытки.
            LastError = ex.Message;
            AppLog.Write($"очередь векторов: нет сети — {ex.Message}");
            SetState(EmbeddingQueueState.Failed);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AppLog.Write($"очередь векторов: упала — {ex}");
            SetState(EmbeddingQueueState.Failed);
        }
        finally
        {
            lock (_gate)
            {
                _running = false;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    private sealed record TrackJob(int TrackId, string Platform, string SourceTrackId, string Url, string Artist);

    private sealed record ArtistJob(int ArtistId, string Nickname);

    private sealed record Plan(List<TrackJob> Tracks, List<ArtistJob> DeezerLookups);

    /// <summary>
    /// Что делать дальше. Считается целиком в памяти: даже 20 тысяч строк треков —
    /// мелочь, а LINQ-to-SQL с группировкой по артисту в SQLite переводится плохо.
    /// </summary>
    private Plan BuildPlan()
    {
        using var db = new AppDbContext();

        var embedded = db.TrackEmbeddings
            .Where(e => e.ModelVersion == MertEmbedder.ModelVersion)
            .Select(e => e.ArtistTrackId)
            .ToHashSet();

        var tracks = db.ArtistTracks
            .Select(t => new
            {
                t.Id, t.ArtistId, t.SourcePlatform, t.SourceTrackId, t.Url,
                t.PlayCount, t.EmbedAttempts, ArtistName = t.Artist!.Nickname
            })
            .ToList();

        var artists = db.Artists
            .Select(a => new { a.Id, a.Nickname, a.DeezerCheckedAt })
            .ToList();

        var jobs = new List<TrackJob>();
        var lookups = new List<ArtistJob>();

        var tracksByArtist = tracks.ToLookup(t => t.ArtistId);

        foreach (var artist in artists)
        {
            var own = tracksByArtist[artist.Id].ToList();
            var have = own.Count(t => embedded.Contains(t.Id));

            var pending = own
                .Where(t => !embedded.Contains(t.Id) &&
                            t.EmbedAttempts < MaxAttempts &&
                            _resolvers.ContainsKey(t.SourcePlatform) &&
                            (!string.IsNullOrWhiteSpace(t.SourceTrackId) || !string.IsNullOrWhiteSpace(t.Url)))
                .OrderBy(t => t.EmbedAttempts)
                .ThenByDescending(t => t.PlayCount)
                .Take(Math.Max(0, TracksPerArtist - have))
                .ToList();

            jobs.AddRange(pending.Select(t =>
                new TrackJob(t.Id, t.SourcePlatform, t.SourceTrackId, t.Url ?? string.Empty, t.ArtistName)));

            if (have == 0 && pending.Count == 0 && string.IsNullOrWhiteSpace(artist.DeezerCheckedAt))
                lookups.Add(new ArtistJob(artist.Id, artist.Nickname));
        }

        return new Plan(jobs, lookups);
    }

    private async Task<bool> EnsureModelAsync(CancellationToken ct)
    {
        if (ModelStore.IsModelReady())
            return true;

        ModelProgress = 0;
        SetState(EmbeddingQueueState.DownloadingModel);

        try
        {
            var progress = new Progress<double>(value =>
            {
                ModelProgress = value;
                StateChanged?.Invoke();
            });

            await ModelStore.DownloadModelAsync(progress, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Без модели слушать нечем. Попытки треков не тратим — дело не в них.
            LastError = ex.Message;
            AppLog.Write($"очередь векторов: не скачалась модель — {ex.Message}");
            SetState(EmbeddingQueueState.Failed);
            return false;
        }
    }

    private async Task ProcessTrackAsync(TrackJob job, CancellationToken ct)
    {
        string? path = null;

        try
        {
            var audio = await _resolvers[job.Platform].ResolveAsync(job.SourceTrackId, job.Url, ct);
            if (audio is null)
            {
                MarkFailed(job.TrackId, "звук недоступен", permanent: true);
                return;
            }

            path = await AudioFetcher.DownloadAsync(audio, TempDirectory, ct);

            var samples = AudioDecoder.Decode(path);
            var seconds = (double)samples.Length / AudioDecoder.TargetSampleRate;

            if (seconds < MinSeconds)
            {
                MarkFailed(job.TrackId, $"короче {MinSeconds} секунд", permanent: true);
                return;
            }

            var raw = MertEmbedder.GetShared(ModelStore.ModelPath).EmbedRaw(samples, ct);
            SaveVector(job.TrackId, raw, audio.AudioKind, seconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Слишком длинный файл или удалённый трек — повтор ничего не даст.
            var permanent = ex is InvalidDataException ||
                            ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Gone };
            MarkFailed(job.TrackId, ex.Message, permanent);
            AppLog.Write($"очередь векторов: {job.Artist} / {job.Platform} {job.SourceTrackId}: {ex.Message}");
        }
        finally
        {
            if (path is not null)
                AudioFetcher.TryDelete(path);
        }
    }

    private async Task LookupDeezerAsync(ArtistJob artist, CancellationToken ct)
    {
        IReadOnlyList<TrackInfo> found;

        try
        {
            found = await _deezer.FindArtistTracksAsync(artist.Nickname, DeezerTracksPerArtist, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Сеть моргнула — пометку не ставим, попробуем в следующий раз.
            AppLog.Write($"очередь векторов: Deezer не ответил по {artist.Nickname}: {ex.Message}");
            return;
        }

        using var db = new AppDbContext();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        foreach (var track in found)
        {
            if (string.IsNullOrWhiteSpace(track.Url) || db.ArtistTracks.Any(t => t.Url == track.Url))
                continue;

            db.ArtistTracks.Add(new ArtistTrack
            {
                ArtistId = artist.ArtistId,
                Title = track.Title,
                Url = track.Url,
                SourcePlatform = _deezer.Platform,
                SourceTrackId = track.SourceId,
                PlayCount = track.PlayCount,
                FetchedAt = now
            });
        }

        var row = db.Artists.FirstOrDefault(a => a.Id == artist.ArtistId);
        if (row is not null)
            row.DeezerCheckedAt = now;

        db.SaveChanges();
        AppLog.Write($"очередь векторов: Deezer по {artist.Nickname} — треков с превью {found.Count}");
    }

    private static void SaveVector(int trackId, float[] raw, string audioKind, double seconds)
    {
        using var db = new AppDbContext();

        var existing = db.TrackEmbeddings.FirstOrDefault(e =>
            e.ArtistTrackId == trackId && e.ModelVersion == MertEmbedder.ModelVersion);

        if (existing is null)
        {
            existing = new TrackEmbedding { ArtistTrackId = trackId, ModelVersion = MertEmbedder.ModelVersion };
            db.TrackEmbeddings.Add(existing);
        }

        existing.Vector = MertEmbedder.ToBytes(raw);
        existing.AudioKind = audioKind;
        existing.Seconds = Math.Round(seconds, 1);
        existing.ComputedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        var track = db.ArtistTracks.FirstOrDefault(t => t.Id == trackId);
        if (track is not null)
            track.EmbedError = string.Empty;

        db.SaveChanges();
    }

    private static void MarkFailed(int trackId, string error, bool permanent)
    {
        using var db = new AppDbContext();

        var track = db.ArtistTracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null)
            return;

        track.EmbedAttempts = permanent ? MaxAttempts : track.EmbedAttempts + 1;
        track.EmbedError = error.Length > 300 ? error[..300] : error;
        db.SaveChanges();
    }

    /// <summary>Файлы, оставшиеся от прерванного прошлого запуска.</summary>
    private static void CleanTempDirectory()
    {
        try
        {
            if (!Directory.Exists(TempDirectory))
                return;

            foreach (var file in Directory.EnumerateFiles(TempDirectory, "*.mp3"))
                AudioFetcher.TryDelete(file);
        }
        catch
        {
            // Не критично: следующий запуск попробует снова.
        }
    }

    private void SetState(EmbeddingQueueState state)
    {
        State = state;
        StateChanged?.Invoke();
    }
}

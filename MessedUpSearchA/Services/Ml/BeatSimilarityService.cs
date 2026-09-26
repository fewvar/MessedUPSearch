using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Ml;

/// <summary>Ориентир стиля (крупный артист из индекса друга) или цель — кому можно предложить бит.</summary>
public enum MatchKind
{
    Target,
    Reference
}

public class ArtistMatch
{
    public string Artist { get; init; } = string.Empty;

    public MatchKind Kind { get; init; } = MatchKind.Target;

    /// <summary>Для артистов большого индекса: откуда он и как его импортировать и послушать.</summary>
    public string Platform { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string AvatarUrl { get; init; } = string.Empty;
    public int Plays { get; init; }
    public string TopTrackId { get; init; } = string.Empty;
    public string TopTrackUrl { get; init; } = string.Empty;
    public string TopTrackTitle { get; init; } = string.Empty;

    /// <summary>Артист из нашей базы, если совпадение пришло из его треков. Для индекса — null.</summary>
    public int? ArtistId { get; init; }

    /// <summary>Сырая косинусная близость, от -1 до 1. Для отладки, не для показа.</summary>
    public float Similarity { get; init; }

    /// <summary>Растянутая под восприятие оценка 0..100 — вот её показываем.</summary>
    public int Percent { get; init; }
}

/// <summary>Результат анализа: «звучит как» и «кому предложить».</summary>
public sealed class SimilarityResult
{
    public IReadOnlyList<ArtistMatch> References { get; init; } = [];
    public IReadOnlyList<ArtistMatch> Targets { get; init; } = [];
}

/// <summary>Трек артиста из базы: сырой вектор MERT, посчитанный по звуку из сети.</summary>
public class ReferenceTrack
{
    public string Artist { get; init; } = string.Empty;
    public int? ArtistId { get; init; }
    public float[] RawVector { get; init; } = [];
}

/// <summary>
/// Бит -> список артистов, на которых он похож по звучанию.
///
/// Порядок действий ровно тот же, что в Python при построении базы, иначе числа
/// не сойдутся: декодируем в 24 кГц моно -> режем на окна по 10 секунд ->
/// прогоняем через MERT -> усредняем окна -> вычитаем центр базы -> нормируем ->
/// считаем косинусы с треками и агрегируем по артистам.
///
/// Треки сравнения — это artist_index.bin (32 артиста из собранного архива) плюс
/// треки артистов из базы, которые приложение само послушало в сети. Центр один
/// на всех — из индекса: он посчитан по 1685 трекам нужных жанров, и пересчитывать
/// его при каждом новом артисте значило бы сдвигать все сохранённые оценки.
/// </summary>
public class BeatSimilarityService : IDisposable
{
    private const int TopTracksPerArtist = 3;

    private readonly MertEmbedder _embedder;
    private readonly bool _ownsEmbedder;
    private readonly ArtistIndex _index;
    private readonly Dictionary<int, List<int>> _tracksByArtist;

    public BeatSimilarityService(MertEmbedder embedder, string indexPath)
    {
        _embedder = embedder;
        _index = ArtistIndex.Load(indexPath);
        _tracksByArtist = _index.GroupByArtist();
    }

    /// <summary>Своя сессия ONNX — для консольной сверки, где общей нет.</summary>
    public BeatSimilarityService(string modelPath, string indexPath)
        : this(new MertEmbedder(modelPath), indexPath)
        => _ownsEmbedder = true;

    public IReadOnlyList<string> KnownArtists => _index.ArtistNames;

    public float[] Center => _index.Center;

    public Task<IReadOnlyList<ArtistMatch>> AnalyzeAsync(
        string audioPath, int top = 5, CancellationToken ct = default)
        => Task.Run(() => Analyze(audioPath, top, ct), ct);

    public IReadOnlyList<ArtistMatch> Analyze(
        string audioPath, int top = 5, CancellationToken ct = default,
        IReadOnlyList<ReferenceTrack>? extraTracks = null)
    {
        var raw = _embedder.EmbedRaw(AudioDecoder.Decode(audioPath), ct);
        var embedding = MertEmbedder.Centered(raw, _index.Center);
        return Rank(embedding, top, extraTracks ?? []);
    }

    /// <summary>
    /// Полная выдача для приложения. Ориентиры — только индекс друга (Yeat, Osamason…):
    /// им бит не продашь, но «звучит как» помогает понять выдачу. Цели — большой индекс
    /// доступных артистов плюс артисты из базы пользователя. Если артист из индекса уже
    /// в базе (совпала ссылка на профиль), это один кандидат с номером из базы.
    /// </summary>
    public SimilarityResult AnalyzeAll(
        string audioPath,
        TargetIndex? targets,
        IReadOnlyList<ReferenceTrack> userTracks,
        IReadOnlyDictionary<string, int> userArtistIdsByUrl,
        int referenceTop = 3,
        int targetTop = 10,
        CancellationToken ct = default)
    {
        var raw = _embedder.EmbedRaw(AudioDecoder.Decode(audioPath), ct);
        var embedding = MertEmbedder.Centered(raw, _index.Center);

        var references = Rank(embedding, referenceTop, [])
            .Select(m => new ArtistMatch
            {
                Artist = m.Artist, Kind = MatchKind.Reference, Similarity = m.Similarity, Percent = m.Percent
            })
            .ToArray();

        var candidates = new Dictionary<string, Candidate>();
        var info = new Dictionary<string, TargetArtist>();

        // Индекс v2 собирается с тем же центром; если нет — векторы несравнимы, лучше без него.
        if (targets is not null && targets.Center.AsSpan().SequenceEqual(_index.Center))
        {
            foreach (var artist in targets.Artists)
            {
                int? id = userArtistIdsByUrl.TryGetValue(artist.SourceUrl, out var found) ? found : null;
                var key = id is { } known ? $"db:{known}" : $"url:{artist.SourceUrl}";

                var candidate = GetCandidate(candidates, key, artist.Nickname, id);
                info[key] = artist;

                foreach (var vector in artist.Vectors)
                    candidate.Add(Dot(embedding, vector));
            }
        }

        foreach (var track in userTracks)
        {
            if (track.ArtistId is not { } id || track.RawVector.Length != embedding.Length)
                continue;

            var candidate = GetCandidate(candidates, $"db:{id}", track.Artist, id);
            candidate.Add(Dot(embedding, MertEmbedder.Centered(track.RawVector, _index.Center)));
        }

        var targetMatches = candidates
            .Where(c => c.Value.Count > 0)
            .Select(c => (Key: c.Key, Candidate: c.Value, Score: c.Value.Score()))
            .OrderByDescending(c => c.Score)
            .Take(targetTop)
            .Select(c =>
            {
                info.TryGetValue(c.Key, out var artist);
                return new ArtistMatch
                {
                    // Ник из базы пользователя главнее: он мог его переименовать.
                    Artist = c.Candidate.ArtistId is not null ? c.Candidate.Name : artist?.Nickname ?? c.Candidate.Name,
                    ArtistId = c.Candidate.ArtistId,
                    Kind = MatchKind.Target,
                    Similarity = c.Score,
                    Percent = ToPercent(c.Score),
                    Platform = artist?.Platform ?? string.Empty,
                    SourceId = artist?.SourceId ?? string.Empty,
                    SourceUrl = artist?.SourceUrl ?? string.Empty,
                    AvatarUrl = artist?.AvatarUrl ?? string.Empty,
                    Plays = artist?.Plays ?? 0,
                    TopTrackId = artist?.TopTrackId ?? string.Empty,
                    TopTrackUrl = artist?.TopTrackUrl ?? string.Empty,
                    TopTrackTitle = artist?.TopTrackTitle ?? string.Empty
                };
            })
            .ToArray();

        return new SimilarityResult { References = references, Targets = targetMatches };
    }

    private IReadOnlyList<ArtistMatch> Rank(float[] embedding, int top, IReadOnlyList<ReferenceTrack> extraTracks)
    {
        // Кандидат — это артист; у него копятся косинусы со всеми его треками.
        // Артиста из базы с тем же именем, что в индексе, сливаем с ним: иначе
        // в топе окажутся два одинаковых «Yeat».
        var groups = new Dictionary<string, Candidate>();

        foreach (var (artistId, trackIds) in _tracksByArtist)
        {
            var name = _index.ArtistNames[artistId];
            var candidate = GetCandidate(groups, NameKey(name), name, null);

            foreach (var trackId in trackIds)
                candidate.Add(Dot(embedding, _index.Vectors[trackId]));
        }

        foreach (var track in extraTracks)
        {
            if (track.RawVector.Length != embedding.Length)
                continue;   // вектор от другой модели — сравнивать нечего

            var nameKey = NameKey(track.Artist);
            var key = groups.ContainsKey(nameKey) || track.ArtistId is null ? nameKey : $"db:{track.ArtistId}";

            var candidate = GetCandidate(groups, key, track.Artist, track.ArtistId);
            candidate.ArtistId ??= track.ArtistId;
            candidate.Add(Dot(embedding, MertEmbedder.Centered(track.RawVector, _index.Center)));
        }

        return groups.Values
            .Where(c => c.Count > 0)
            .Select(c => (c.Name, c.ArtistId, Score: c.Score()))
            .OrderByDescending(c => c.Score)
            .Take(top)
            .Select(c => new ArtistMatch
            {
                Artist = c.Name,
                ArtistId = c.ArtistId,
                Similarity = c.Score,
                Percent = ToPercent(c.Score)
            })
            .ToArray();
    }

    private static Candidate GetCandidate(Dictionary<string, Candidate> groups, string key, string name, int? artistId)
    {
        if (!groups.TryGetValue(key, out var candidate))
            groups[key] = candidate = new Candidate { Name = name, ArtistId = artistId };
        return candidate;
    }

    private static string NameKey(string name) =>
        "name:" + new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// Оценка артиста — среднее по трём его ближайшим трекам. Один трек
    /// слишком случаен, среднее по всем размывает: у Yeat 153 трека.
    /// </summary>
    private sealed class Candidate
    {
        private readonly float[] _best = [float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity];

        public string Name { get; init; } = string.Empty;
        public int? ArtistId { get; set; }
        public int Count { get; private set; }

        public void Add(float similarity)
        {
            Count++;

            for (var slot = 0; slot < TopTracksPerArtist; slot++)
            {
                if (similarity <= _best[slot])
                    continue;

                for (var shift = TopTracksPerArtist - 1; shift > slot; shift--)
                    _best[shift] = _best[shift - 1];

                _best[slot] = similarity;
                break;
            }
        }

        public float Score() => _best.Where(v => !float.IsNegativeInfinity(v)).Average();
    }

    /// <summary>
    /// Косинус в проценты. Честного способа нет: даже у чужих друг другу треков
    /// близость редко уходит сильно ниже нуля, а у похожих редко превышает 0.6.
    /// Поэтому растягиваем рабочий диапазон 0.05..0.65 на всю шкалу — это подгонка
    /// под восприятие, а не физическая величина.
    /// </summary>
    private static int ToPercent(float similarity)
    {
        const float low = 0.05f;
        const float high = 0.65f;

        var normalized = (similarity - low) / (high - low);
        return (int)Math.Round(Math.Clamp(normalized, 0f, 1f) * 100);
    }

    private static float Dot(float[] a, float[] b)
    {
        var sum = 0f;
        for (var i = 0; i < a.Length; i++)
            sum += a[i] * b[i];
        return sum;
    }

    public void Dispose()
    {
        if (_ownsEmbedder)
            _embedder.Dispose();
    }
}

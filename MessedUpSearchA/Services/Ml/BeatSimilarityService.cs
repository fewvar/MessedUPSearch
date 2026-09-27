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
    /// Полная выдача для приложения, по инструменталам (см. ml/README.md, шаг 2 плана качества).
    ///
    /// Ориентиры — references_v2.bin: крупные артисты, им бит не продашь, но «звучит как»
    /// помогает понять выдачу. Цели — artists_index_v2.bin, доступные артисты. У обоих
    /// индексов один центр — среднее инструменталов; бит центрируется им же. Бит считается
    /// как обычно: в нём и так нет голоса, отделять нечего.
    ///
    /// Артисты из базы пользователя в выдаче — только если они есть в индексе (совпала
    /// ссылка на профиль): их собственные векторы считались по трекам с голосом и
    /// с инструменталами индекса несравнимы.
    /// </summary>
    public SimilarityResult AnalyzeAll(
        string audioPath,
        TargetIndex references,
        TargetIndex? targets,
        IReadOnlyDictionary<string, int> userArtistIdsByUrl,
        HubCorrection? hubs,
        int referenceTop = 3,
        int targetTop = 10,
        CancellationToken ct = default)
    {
        var raw = _embedder.EmbedRaw(AudioDecoder.Decode(audioPath), ct);
        var embedding = MertEmbedder.Centered(raw, references.Center);

        var referenceMatches = RankIndex(embedding, references, "ref", hubs, referenceTop, userArtistIdsByUrl)
            .Select(m => new ArtistMatch
            {
                Artist = m.Artist, Kind = MatchKind.Reference, Similarity = m.Similarity, Percent = m.Percent
            })
            .ToArray();

        // Индекс целей собирается с тем же центром; если нет — векторы несравнимы, лучше без него.
        var targetMatches = targets is not null && targets.Center.AsSpan().SequenceEqual(references.Center)
            ? RankIndex(embedding, targets, "url", hubs, targetTop, userArtistIdsByUrl)
            : [];

        return new SimilarityResult { References = referenceMatches, Targets = targetMatches };
    }

    private static ArtistMatch[] RankIndex(
        float[] embedding, TargetIndex index, string keyPrefix, HubCorrection? hubs, int top,
        IReadOnlyDictionary<string, int> userArtistIdsByUrl)
    {
        var scored = new List<(TargetArtist Artist, float Score)>(index.Artists.Count);

        foreach (var artist in index.Artists)
        {
            if (artist.Vectors.Length == 0 || artist.Vectors[0].Length != embedding.Length)
                continue;

            var score = TopTracksScore(embedding, artist.Vectors);
            if (hubs is not null)
                score -= hubs.Bias($"{keyPrefix}:{artist.SourceUrl}:{artist.Nickname}", artist.Vectors, TopTracksScore);

            scored.Add((artist, score));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .Take(top)
            .Select(s =>
            {
                int? id = s.Artist.SourceUrl.Length > 0 && userArtistIdsByUrl.TryGetValue(s.Artist.SourceUrl, out var found)
                    ? found
                    : null;

                return new ArtistMatch
                {
                    Artist = s.Artist.Nickname,
                    ArtistId = id,
                    Kind = MatchKind.Target,
                    Similarity = s.Score,
                    Percent = hubs is null ? ToPercent(s.Score) : HubCorrection.ToPercent(s.Score),
                    Platform = s.Artist.Platform,
                    SourceId = s.Artist.SourceId,
                    SourceUrl = s.Artist.SourceUrl,
                    AvatarUrl = s.Artist.AvatarUrl,
                    Plays = s.Artist.Plays,
                    TopTrackId = s.Artist.TopTrackId,
                    TopTrackUrl = s.Artist.TopTrackUrl,
                    TopTrackTitle = s.Artist.TopTrackTitle
                };
            })
            .ToArray();
    }

    /// <summary>Оценка артиста — среднее по трём его ближайшим к биту трекам (как Candidate.Score).</summary>
    private static float TopTracksScore(float[] beat, IReadOnlyList<float[]> vectors)
    {
        var best = new float[Math.Min(TopTracksPerArtist, vectors.Count)];
        Array.Fill(best, float.NegativeInfinity);

        foreach (var vector in vectors)
        {
            var similarity = Dot(beat, vector);
            for (var slot = 0; slot < best.Length; slot++)
            {
                if (similarity <= best[slot])
                    continue;
                for (var shift = best.Length - 1; shift > slot; shift--)
                    best[shift] = best[shift - 1];
                best[slot] = similarity;
                break;
            }
        }

        return best.Average();
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

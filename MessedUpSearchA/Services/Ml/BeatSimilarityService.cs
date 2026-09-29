using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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

/// <summary>
/// Бит -> «звучит как» (ориентиры) и «кому предложить» (большой индекс).
///
/// Бит считается EffNet + голова (<see cref="EffNetEmbedder"/>) — тем же конвейером, что векторы
/// индексов v3, поэтому сравнение — просто косинус. Оценка артиста — среднее по трём его ближайшим
/// трекам минус его средняя оценка по фону (поправка на хабы).
///
/// Артисты из базы пользователя в выдаче — только если они есть в индексе (совпала ссылка на профиль).
/// </summary>
public class BeatSimilarityService
{
    private const int TopTracksPerArtist = 3;

    private readonly EffNetEmbedder _embedder;

    public BeatSimilarityService(EffNetEmbedder embedder) => _embedder = embedder;

    public SimilarityResult AnalyzeAll(
        string audioPath,
        TargetIndex references,
        TargetIndex? targets,
        IReadOnlyDictionary<string, int> userArtistIdsByUrl,
        HubCorrection? hubs,
        int referenceTop = 3,
        int targetTop = 10,
        CancellationToken ct = default)
        => Rank(_embedder.EmbedFile(audioPath, ct), references, targets, userArtistIdsByUrl, hubs, referenceTop, targetTop);

    /// <summary>Готовый вектор бита -> выдача. Отдельно — чтобы краулер и сверка звали без звука.</summary>
    public static SimilarityResult Rank(
        float[] embedding,
        TargetIndex references,
        TargetIndex? targets,
        IReadOnlyDictionary<string, int> userArtistIdsByUrl,
        HubCorrection? hubs,
        int referenceTop = 3,
        int targetTop = 10)
    {
        var referenceMatches = RankIndex(embedding, references, "ref", hubs, referenceTop, userArtistIdsByUrl)
            .Select(m => new ArtistMatch
            {
                Artist = m.Artist, Kind = MatchKind.Reference, Similarity = m.Similarity, Percent = m.Percent
            })
            .ToArray();

        // Индекс целей из другой сборки головы (другой центр) несравним — лучше без него.
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
                    Percent = HubCorrection.ToPercent(s.Score, reference: keyPrefix == "ref"),
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

    private static float Dot(float[] a, float[] b)
    {
        var sum = 0f;
        for (var i = 0; i < a.Length; i++)
            sum += a[i] * b[i];
        return sum;
    }
}

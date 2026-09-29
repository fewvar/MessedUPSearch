using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MessedUpSearchA.Data;
using MessedUpSearchA.Services.Parsing.Sources;

namespace MessedUpSearchA.Services.Parsing;

/// <summary>
/// ➕ в выдаче: артист из большого индекса -> в базу пользователя тем же путём, что и
/// парсер (профиль, аватарка, треки). Треки берём свежие с площадки, а не из индекса:
/// там их нет, только векторы.
/// </summary>
public static class IndexArtistImporter
{
    /// <returns>Номер артиста в базе.</returns>
    public static async Task<int> ImportAsync(
        string platform, string sourceId, string sourceUrl, string nickname, string avatarUrl,
        CancellationToken ct = default)
    {
        IArtistSource source = platform switch
        {
            "SoundCloud" => new SoundCloudSource(),
            "Audius" => new AudiusSource(),
            _ => throw new NotSupportedException($"импорт с площадки {platform} не поддерживается")
        };

        var candidate = new ArtistCandidate
        {
            Platform = platform,
            SourceId = sourceId,
            SourceUrl = sourceUrl,
            Nickname = nickname,
            AvatarUrl = avatarUrl
        };

        var tracks = await source.GetTracksAsync(candidate, 10, ct);
        candidate.Tracks.AddRange(tracks);
        candidate.TotalPlays = tracks.Sum(t => t.PlayCount);

        var folder = AppSettings.Load().ResolveMediaFolder();
        var outcome = await new ArtistImportService(folder).ImportAsync(new[] { candidate }, ct);

        if (outcome.Problems.Count > 0 && outcome.Added + outcome.Updated == 0)
            throw new InvalidOperationException(outcome.Problems[0]);

        using var db = new AppDbContext();
        return db.Artists.Where(a => a.SourceUrl == sourceUrl).Select(a => a.Id).First();
    }
}

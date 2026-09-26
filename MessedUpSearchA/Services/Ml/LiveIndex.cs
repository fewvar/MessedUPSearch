using System.Collections.Generic;
using System.Linq;
using MessedUpSearchA.Data;

namespace MessedUpSearchA.Services.Ml;

/// <summary>
/// Векторы треков артистов из базы — вторая половина того, с чем сравнивается бит.
/// Читается при каждом анализе заново: очередь дописывает их в фоне, и держать
/// кэш значило бы пропускать только что послушанных артистов.
/// 20 тысяч треков — это 60 МБ BLOB-ов и доли секунды на чтение.
/// </summary>
public static class LiveIndex
{
    public static IReadOnlyList<ReferenceTrack> Load()
    {
        using var db = new AppDbContext();

        return (from e in db.TrackEmbeddings
                where e.ModelVersion == MertEmbedder.ModelVersion
                join t in db.ArtistTracks on e.ArtistTrackId equals t.Id
                join a in db.Artists on t.ArtistId equals a.Id
                select new { a.Id, a.Nickname, e.Vector })
            .AsEnumerable()
            .Select(r => new ReferenceTrack
            {
                Artist = r.Nickname,
                ArtistId = r.Id,
                RawVector = MertEmbedder.FromBytes(r.Vector)
            })
            .ToList();
    }

    /// <summary>Есть ли у артиста хоть один вектор — то есть знает ли его модель.</summary>
    public static bool HasVectors(int artistId)
    {
        using var db = new AppDbContext();

        return db.TrackEmbeddings.Any(e =>
            e.ModelVersion == MertEmbedder.ModelVersion &&
            db.ArtistTracks.Any(t => t.Id == e.ArtistTrackId && t.ArtistId == artistId));
    }
}

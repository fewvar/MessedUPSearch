using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MessedUpSearchA.Services.Ml;

/// <summary>Трек артиста в индексе: чтобы ▶ сыграл тот, что ближе всего к биту.</summary>
public sealed record TargetTrack(string Id, string Url, string Title);

/// <summary>Артист большого индекса: кому можно предложить бит.</summary>
public sealed class TargetArtist
{
    public string Nickname { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string AvatarUrl { get; init; } = string.Empty;
    public string Genre { get; init; } = string.Empty;
    public int Plays { get; init; }

    /// <summary>Самый прослушиваемый трек — запасной для ▶, если у векторов нет своих треков (индекс v3).</summary>
    public string TopTrackId { get; init; } = string.Empty;
    public string TopTrackUrl { get; init; } = string.Empty;
    public string TopTrackTitle { get; init; } = string.Empty;

    /// <summary>Векторы треков — уже центрированные тем же центром и нормированные.</summary>
    public float[][] Vectors { get; init; } = [];

    /// <summary>Трек каждого вектора (тот же порядок). В v3 пусто.</summary>
    public TargetTrack[] Tracks { get; init; } = [];
}

/// <summary>
/// Индекс артистов: и большой (artists_index_v3.bin, собирает ml/crawler — кому можно написать),
/// и ориентиры «звучит как» (references_v3.bin, ml/scripts/export_effnet.py).
///
/// Формат (little-endian):
///   "MUSX", int версия = 3, int размерность, int артистов, float[размерность] центр,
///   на артиста: 9 строк (int длина + UTF-8: ник, площадка, id, ссылка, аватар, жанр,
///   id лучшего трека, ссылка на него, его название),
///   int прослушивания, int треков, затем треки × размерность × Half.
/// Half вместо float: индекс вдвое меньше, а на косинусе разницы не видно.
/// v4 (1.0.1): после векторов артиста — по три строки на трек (id, ссылка, название): ▶ в выдаче
/// играет трек, ближайший к биту, а не самый популярный. v3 читается как раньше (без треков).
/// v3: векторы EffNet + голова, уже финальные (центр c2 из головы — для справки и сверки
/// «индекс и ориентиры из одной сборки»). v2 (MERT) приложение больше не читает.
/// </summary>
public sealed class TargetIndex
{
    private const string Magic = "MUSX";
    private const int Version = 4;
    private const int OldestReadable = 3;

    public float[] Center { get; init; } = [];
    public IReadOnlyList<TargetArtist> Artists { get; init; } = [];

    public int Dimension => Center.Length;

    public static TargetIndex Load(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);

        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != Magic)
            throw new InvalidDataException("это не индекс артистов");

        var version = reader.ReadInt32();
        if (version < OldestReadable || version > Version)
            throw new InvalidDataException($"индекс версии {version}, приложение понимает {Version} — обнови приложение");

        var dimension = reader.ReadInt32();
        var count = reader.ReadInt32();
        if (dimension <= 0 || dimension > 8192 || count < 0)
            throw new InvalidDataException("битый индекс");

        var center = new float[dimension];
        for (var i = 0; i < dimension; i++)
            center[i] = reader.ReadSingle();

        var artists = new List<TargetArtist>(count);
        for (var a = 0; a < count; a++)
        {
            var nickname = ReadString(reader);
            var platform = ReadString(reader);
            var sourceId = ReadString(reader);
            var sourceUrl = ReadString(reader);
            var avatarUrl = ReadString(reader);
            var genre = ReadString(reader);
            var topTrackId = ReadString(reader);
            var topTrackUrl = ReadString(reader);
            var topTrackTitle = ReadString(reader);
            var plays = reader.ReadInt32();
            var tracks = reader.ReadInt32();

            var vectors = new float[tracks][];
            for (var t = 0; t < tracks; t++)
            {
                var vector = new float[dimension];
                for (var i = 0; i < dimension; i++)
                    vector[i] = (float)BitConverter.UInt16BitsToHalf(reader.ReadUInt16());
                vectors[t] = vector;
            }

            var trackInfo = new TargetTrack[version >= 4 ? tracks : 0];
            for (var t = 0; t < trackInfo.Length; t++)
                trackInfo[t] = new TargetTrack(ReadString(reader), ReadString(reader), ReadString(reader));

            artists.Add(new TargetArtist
            {
                Nickname = nickname, Platform = platform, SourceId = sourceId, SourceUrl = sourceUrl,
                AvatarUrl = avatarUrl, Genre = genre, Plays = plays, Vectors = vectors, Tracks = trackInfo,
                TopTrackId = topTrackId, TopTrackUrl = topTrackUrl, TopTrackTitle = topTrackTitle
            });
        }

        return new TargetIndex { Center = center, Artists = artists };
    }

    public void Save(string path)
    {
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8);

        writer.Write(Encoding.ASCII.GetBytes(Magic));
        writer.Write(Version);
        writer.Write(Dimension);
        writer.Write(Artists.Count);
        foreach (var value in Center)
            writer.Write(value);

        foreach (var artist in Artists)
        {
            foreach (var text in new[] { artist.Nickname, artist.Platform, artist.SourceId, artist.SourceUrl, artist.AvatarUrl,
                         artist.Genre, artist.TopTrackId, artist.TopTrackUrl, artist.TopTrackTitle })
                WriteString(writer, text);

            writer.Write(artist.Plays);
            writer.Write(artist.Vectors.Length);

            foreach (var vector in artist.Vectors)
            {
                if (vector.Length != Dimension)
                    throw new InvalidDataException($"{artist.Nickname}: вектор {vector.Length} вместо {Dimension}");

                foreach (var value in vector)
                    writer.Write(BitConverter.HalfToUInt16Bits((Half)value));
            }

            // v4: трек каждого вектора. Нет данных — пустые строки (▶ тогда возьмёт самый популярный).
            for (var t = 0; t < artist.Vectors.Length; t++)
            {
                var track = t < artist.Tracks.Length ? artist.Tracks[t] : new TargetTrack("", "", "");
                WriteString(writer, track.Id);
                WriteString(writer, track.Url);
                WriteString(writer, track.Title);
            }
        }
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > 4096)
            throw new InvalidDataException("битая строка в индексе");
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}

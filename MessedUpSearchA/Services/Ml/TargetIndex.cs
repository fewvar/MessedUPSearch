using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MessedUpSearchA.Services.Ml;

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

    /// <summary>Векторы треков — уже центрированные тем же центром и нормированные.</summary>
    public float[][] Vectors { get; init; } = [];
}

/// <summary>
/// Большой индекс доступных артистов (artists_index_v2.bin), собранный краулером
/// из ml/crawler. Отдельно от artist_index.bin: там 32 крупных артиста — ориентиры
/// стиля, здесь — те, кому реально можно написать.
///
/// Формат (little-endian):
///   "MUSX", int версия = 2, int размерность, int артистов, float[размерность] центр,
///   на артиста: 6 строк (int длина + UTF-8: ник, площадка, id, ссылка, аватар, жанр),
///   int прослушивания, int треков, затем треки × размерность × Half.
/// Half вместо float: индекс вдвое меньше, а на косинусе разницы не видно.
/// </summary>
public sealed class TargetIndex
{
    private const string Magic = "MUSX";
    private const int Version = 2;

    public float[] Center { get; init; } = [];
    public IReadOnlyList<TargetArtist> Artists { get; init; } = [];

    public int Dimension => Center.Length;

    public static TargetIndex Load(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);

        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != Magic)
            throw new InvalidDataException("это не индекс артистов v2");

        var version = reader.ReadInt32();
        if (version != Version)
            throw new InvalidDataException($"индекс версии {version}, приложение понимает {Version} — обнови приложение");

        var dimension = reader.ReadInt32();
        var count = reader.ReadInt32();
        if (dimension <= 0 || dimension > 8192 || count < 0)
            throw new InvalidDataException("битый индекс v2");

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

            artists.Add(new TargetArtist
            {
                Nickname = nickname, Platform = platform, SourceId = sourceId, SourceUrl = sourceUrl,
                AvatarUrl = avatarUrl, Genre = genre, Plays = plays, Vectors = vectors
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
            foreach (var text in new[] { artist.Nickname, artist.Platform, artist.SourceId, artist.SourceUrl, artist.AvatarUrl, artist.Genre })
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
        }
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > 4096)
            throw new InvalidDataException("битая строка в индексе v2");
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}

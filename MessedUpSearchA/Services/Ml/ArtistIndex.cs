using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MessedUpSearchA.Services.Ml;

/// <summary>
/// Векторы 1685 треков 32 артистов, посчитанные заранее в Python.
/// Формат описан в ml/scripts/export_mert_onnx.py — менять его надо в обоих местах.
/// </summary>
public class ArtistIndex
{
    private ArtistIndex(float[] center, float[][] vectors, string[] artistNames, int[] trackArtistIds)
    {
        Center = center;
        Vectors = vectors;
        ArtistNames = artistNames;
        TrackArtistIds = trackArtistIds;
    }

    /// <summary>Средний вектор базы. Его вычитают из эмбеддинга бита перед сравнением.</summary>
    public float[] Center { get; }

    public float[][] Vectors { get; }

    public string[] ArtistNames { get; }

    public int[] TrackArtistIds { get; }

    public int Dimension => Center.Length;

    public static ArtistIndex Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        var trackCount = reader.ReadInt32();
        var dimension = reader.ReadInt32();

        if (trackCount <= 0 || dimension <= 0 || dimension > 8192)
            throw new InvalidDataException($"битый индекс: {trackCount} треков, размерность {dimension}");

        var center = ReadFloats(reader, dimension);

        var vectors = new float[trackCount][];
        for (var i = 0; i < trackCount; i++)
            vectors[i] = ReadFloats(reader, dimension);

        var artistCount = reader.ReadInt32();
        if (artistCount <= 0 || artistCount > trackCount)
            throw new InvalidDataException($"битый индекс: {artistCount} артистов");

        var names = new string[artistCount];
        for (var i = 0; i < artistCount; i++)
        {
            var length = reader.ReadInt32();
            names[i] = Encoding.UTF8.GetString(reader.ReadBytes(length));
        }

        var trackArtistIds = new int[trackCount];
        for (var i = 0; i < trackCount; i++)
        {
            var id = reader.ReadInt32();
            if (id < 0 || id >= artistCount)
                throw new InvalidDataException($"битый индекс: трек {i} ссылается на артиста {id}");
            trackArtistIds[i] = id;
        }

        return new ArtistIndex(center, vectors, names, trackArtistIds);
    }

    /// <summary>Индексы треков каждого артиста — считается один раз при загрузке.</summary>
    public Dictionary<int, List<int>> GroupByArtist()
    {
        var groups = new Dictionary<int, List<int>>();

        for (var track = 0; track < TrackArtistIds.Length; track++)
        {
            if (!groups.TryGetValue(TrackArtistIds[track], out var list))
                groups[TrackArtistIds[track]] = list = new List<int>();
            list.Add(track);
        }

        return groups;
    }

    private static float[] ReadFloats(BinaryReader reader, int count)
    {
        var bytes = reader.ReadBytes(count * sizeof(float));
        if (bytes.Length != count * sizeof(float))
            throw new EndOfStreamException("индекс оборван");

        var values = new float[count];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }
}

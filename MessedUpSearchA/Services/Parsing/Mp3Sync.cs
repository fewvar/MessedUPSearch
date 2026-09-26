using System;
using System.IO;

namespace MessedUpSearchA.Services.Parsing;

/// <summary>
/// Кусок mp3 из середины файла начинается с обрывка кадра. Декодер ищет ближайшие
/// байты, похожие на заголовок, и иногда цепляется за случайные: на Audius-треке
/// 48 кГц / 320 кбит из середины вышел «звук» 16 кГц — мусор, из которого получился
/// бы мусорный вектор. Поэтому начало ищем сами: место, где подряд идут несколько
/// кадров с одинаковым форматом и каждый следующий стоит ровно там, где кончается предыдущий.
/// </summary>
public static class Mp3Sync
{
    private const int FramesInARow = 4;

    private static readonly int[] BitratesV1 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
    private static readonly int[] BitratesV2 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };

    private static readonly int[][] SampleRates =
    {
        new[] { 11025, 12000, 8000 },   // MPEG 2.5
        Array.Empty<int>(),             // зарезервировано
        new[] { 22050, 24000, 16000 },  // MPEG 2
        new[] { 44100, 48000, 32000 }   // MPEG 1
    };

    /// <summary>Отрезать всё до первого надёжного кадра. false — надёжных кадров нет вовсе.</summary>
    public static bool TrimToFirstFrame(string path)
    {
        var data = File.ReadAllBytes(path);
        var start = FindStart(data);

        if (start < 0)
            return false;

        if (start > 0)
            File.WriteAllBytes(path, data.AsSpan(start).ToArray());

        return true;
    }

    public static int FindStart(ReadOnlySpan<byte> data)
    {
        for (var offset = 0; offset + 4 <= data.Length; offset++)
        {
            if (!TryReadHeader(data, offset, out var first))
                continue;

            var position = offset + first.Length;
            var confirmed = 1;

            while (confirmed < FramesInARow &&
                   TryReadHeader(data, position, out var next) &&
                   next.Version == first.Version &&
                   next.SampleRate == first.SampleRate)
            {
                position += next.Length;
                confirmed++;
            }

            if (confirmed == FramesInARow)
                return offset;
        }

        return -1;
    }

    private readonly record struct Header(int Version, int SampleRate, int Length);

    private static bool TryReadHeader(ReadOnlySpan<byte> data, int offset, out Header header)
    {
        header = default;
        if (offset < 0 || offset + 4 > data.Length)
            return false;

        if (data[offset] != 0xFF || (data[offset + 1] & 0xE0) != 0xE0)
            return false;

        var version = (data[offset + 1] >> 3) & 3;
        var layer = (data[offset + 1] >> 1) & 3;
        var bitrateIndex = data[offset + 2] >> 4;
        var rateIndex = (data[offset + 2] >> 2) & 3;
        var padding = (data[offset + 2] >> 1) & 1;

        // Только Layer III, без «свободного» и запрещённого битрейта и частоты.
        if (version == 1 || layer != 1 || bitrateIndex is 0 or 15 || rateIndex == 3)
            return false;

        var bitrate = (version == 3 ? BitratesV1 : BitratesV2)[bitrateIndex] * 1000;
        var sampleRate = SampleRates[version][rateIndex];
        var length = (version == 3 ? 144 : 72) * bitrate / sampleRate + padding;

        if (length < 24)
            return false;

        header = new Header(version, sampleRate, length);
        return true;
    }
}

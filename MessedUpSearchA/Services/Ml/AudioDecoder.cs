using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NLayer;

namespace MessedUpSearchA.Services.Ml;

/// <summary>
/// WAV/MP3 -> моно float 24 кГц, то есть ровно то, что ждёт модель.
///
/// Ресемплинг здесь не для галочки: эмбеддинги базы считались после ffmpeg, и если
/// подсунуть модели грубо передискретизированный звук, вектор бита уедет в сторону,
/// а вместе с ним и вся выдача. Поэтому не линейная интерполяция, а windowed-sinc —
/// тот же класс качества, что у ffmpeg.
/// </summary>
public static class AudioDecoder
{
    public const int TargetSampleRate = 24000;

    /// <summary>Ширина ядра ресемплера: больше — точнее и медленнее. 16 хватает с запасом.</summary>
    private const int SincHalfWidth = 16;

    public static float[] Decode(string filePath)
    {
        var (samples, sampleRate) = DecodeNative(filePath);
        return sampleRate == TargetSampleRate ? samples : Resample(samples, sampleRate, TargetSampleRate);
    }

    /// <summary>
    /// Моно в родной частоте файла, без ресемплинга. Для волны в плеере этого
    /// хватает, а ресемплинг — больше половины времени всего декодирования.
    /// </summary>
    public static (float[] Samples, int SampleRate) DecodeNative(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("файл не найден", filePath);

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        var (samples, sampleRate) = extension switch
        {
            ".wav" => ReadWav(filePath),
            ".mp3" => ReadMp3(filePath),
            _ => throw new NotSupportedException($"формат {extension} не поддерживается")
        };

        if (samples.Length == 0)
            throw new InvalidDataException("в файле нет звука");

        return (samples, sampleRate);
    }

    private static (float[] Samples, int SampleRate) ReadMp3(string path)
    {
        using var file = new MpegFile(path);

        var channels = file.Channels;
        var buffer = new float[16384];
        var mono = new List<float>((int)Math.Max(1024, file.Length / Math.Max(1, channels)));

        int read;
        while ((read = file.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            if (channels == 1)
            {
                for (var i = 0; i < read; i++)
                    mono.Add(buffer[i]);
            }
            else
            {
                // NLayer отдаёт кадры вперемешку по каналам: L R L R ...
                for (var i = 0; i + channels - 1 < read; i += channels)
                {
                    var sum = 0f;
                    for (var c = 0; c < channels; c++)
                        sum += buffer[i + c];
                    mono.Add(sum / channels);
                }
            }
        }

        return (mono.ToArray(), file.SampleRate);
    }

    private static (float[] Samples, int SampleRate) ReadWav(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII);

        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("не RIFF-файл");

        reader.ReadInt32();

        if (new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("не WAVE-файл");

        int channels = 0, sampleRate = 0, bitsPerSample = 0, formatTag = 0;

        // Чанки идут в произвольном порядке, между fmt и data может лежать что угодно.
        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadInt32();

            if (chunkSize < 0 || stream.Position + chunkSize > stream.Length)
                chunkSize = (int)(stream.Length - stream.Position);

            if (chunkId == "fmt ")
            {
                var chunkEnd = stream.Position + chunkSize;
                formatTag = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();                 // байт в секунду
                reader.ReadInt16();                 // выравнивание блока
                bitsPerSample = reader.ReadInt16();
                stream.Position = chunkEnd;
            }
            else if (chunkId == "data")
            {
                if (channels == 0 || sampleRate == 0)
                    throw new InvalidDataException("data идёт раньше fmt");

                var bytes = reader.ReadBytes(chunkSize);
                return (ToMono(bytes, channels, bitsPerSample, formatTag), sampleRate);
            }
            else
            {
                stream.Position += chunkSize;
            }

            if ((chunkSize & 1) == 1 && stream.Position < stream.Length)
                stream.Position++;              // чанки выравниваются по чётной границе
        }

        throw new InvalidDataException("в файле нет чанка data");
    }

    private static float[] ToMono(byte[] raw, int channels, int bitsPerSample, int formatTag)
    {
        const int PcmFloat = 3;

        var bytesPerSample = bitsPerSample / 8;
        if (bytesPerSample == 0)
            throw new InvalidDataException("неизвестная разрядность");

        var frames = raw.Length / (bytesPerSample * channels);
        var mono = new float[frames];

        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;

            for (var channel = 0; channel < channels; channel++)
            {
                var offset = (frame * channels + channel) * bytesPerSample;

                sum += bitsPerSample switch
                {
                    16 => BitConverter.ToInt16(raw, offset) / 32768f,
                    24 => ((raw[offset + 2] << 24 | raw[offset + 1] << 16 | raw[offset] << 8) >> 8) / 8388608f,
                    32 when formatTag == PcmFloat => BitConverter.ToSingle(raw, offset),
                    32 => BitConverter.ToInt32(raw, offset) / 2147483648f,
                    8 => (raw[offset] - 128) / 128f,
                    _ => throw new InvalidDataException($"разрядность {bitsPerSample} не поддерживается")
                };
            }

            mono[frame] = sum / channels;
        }

        return mono;
    }

    /// <summary>
    /// Windowed-sinc ресемплинг с окном Блэкмана. При понижении частоты ядро
    /// растягивается по новой частоте среза — иначе выше неё полезет алиасинг,
    /// который модель услышит как посторонний звук.
    /// </summary>
    private static float[] Resample(float[] input, int sourceRate, int targetRate)
    {
        var ratio = (double)targetRate / sourceRate;
        var outputLength = (int)(input.Length * ratio);
        if (outputLength <= 0)
            return Array.Empty<float>();

        var output = new float[outputLength];

        // Понижаем частоту — режем спектр по новой Найквистовой частоте.
        var cutoff = ratio < 1.0 ? ratio : 1.0;
        var half = (int)Math.Ceiling(SincHalfWidth / cutoff);

        for (var i = 0; i < outputLength; i++)
        {
            var center = i / ratio;
            var nearest = (int)Math.Floor(center);

            var sum = 0.0;
            var weightSum = 0.0;

            for (var tap = nearest - half + 1; tap <= nearest + half; tap++)
            {
                if (tap < 0 || tap >= input.Length)
                    continue;

                var distance = center - tap;
                var weight = Sinc(distance * cutoff) * Blackman(distance / half);

                sum += input[tap] * weight;
                weightSum += weight;
            }

            output[i] = weightSum > 1e-9 ? (float)(sum / weightSum) : 0f;
        }

        return output;
    }

    private static double Sinc(double x)
    {
        if (Math.Abs(x) < 1e-9)
            return 1.0;

        var pix = Math.PI * x;
        return Math.Sin(pix) / pix;
    }

    private static double Blackman(double x)
    {
        if (Math.Abs(x) >= 1.0)
            return 0.0;

        var t = Math.PI * (x + 1.0);
        return 0.42 - 0.5 * Math.Cos(t) + 0.08 * Math.Cos(2.0 * t);
    }
}
